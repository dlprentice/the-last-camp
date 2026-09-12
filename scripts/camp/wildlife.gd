class_name Wildlife
extends Node3D

## A few birds and small insects occupy specific habitats. Film cues use the
## shot clock, while ordinary play keeps the quieter autonomous visits.

const BIRD_CUE := &"pond_birds"
const MEADOW_CUE := &"meadow_life"
const POND_CUE := &"pond_life"
const BIRD_TAKEOFF := 3.5
const BIRD_FLIGHT_SECONDS := 8.0
const COMPANION_START := 8.0
const COMPANION_END := 12.5
const MEADOW_INSECTS := Vector2(4.3, 10.2)
const POND_INSECTS := Vector2(-28.5, 13.3)

var field: TerrainField
var fireflies: GPUParticles3D
var motes: GPUParticles3D
var _shelter_ready := false
var _life_clock := 0.0
var _fish_clock := 0.0
var _bird_wind_phase := 0.0
var _bird_heights: Array[float] = []
var _birds: Array[Node3D] = []
var _fish: Array[MeshInstance3D] = []
var fish_routes: Array[PackedVector3Array] = []
var _gnats: MultiMeshInstance3D
var _gnat_centres: Array[Vector3] = []
var _butterflies: Array[Node3D] = []
var _dragonflies: Array[Node3D] = []
var _butterfly_anchor := Vector3.ZERO
var _dragonfly_anchor := Vector3.ZERO
var _butterfly_habitats: Array[Vector3] = []
var _dragonfly_habitats: Array[Vector3] = []
var _quality_life_scale := 1.0
var _bird_perch := Vector3.ZERO
var _bird_perch_basis := Basis.IDENTITY
var _perch_ready := false
var _film_controlled := false
var _film_cue: StringName = &""
var _film_seconds := 0.0


func _init(p_field: TerrainField) -> void:
	field = p_field
	name = "Wildlife"


func build() -> void:
	_build_motes()
	_build_fireflies()
	_build_small_life()
	_prepare_bird_perch()
	Quality.preset_changed.connect(apply_quality)
	apply_quality(Quality.current)


## Called each shot frame, including an empty cue for the quiet shots. This
## makes the same event visible in previews and movies regardless of loading
## time. The caller owns camera composition; wildlife stays in world space.
func set_film_cue(cue: StringName, seconds: float) -> void:
	_film_controlled = true
	_film_cue = cue
	_film_seconds = maxf(seconds, 0.0)


func clear_film_cue() -> void:
	_film_controlled = false
	_film_cue = &""


## Feet contact the rendered, tilted post cap. Useful for framing a close
## observation from the dock; the bird itself is not enlarged for the film.
func bird_perch_position() -> Vector3:
	return to_global(_bird_perch)


func insect_focus(cue: StringName) -> Vector3:
	return to_global(_dragonfly_anchor if cue == POND_CUE else _butterfly_anchor)


func _process(delta: float) -> void:
	_life_clock += delta
	if not _shelter_ready and Game.camp != null and Game.camp.campsite != null:
		var tent: Tent = Game.camp.campsite.tent
		if tent != null:
			motes.material_override.set_shader_parameter("tent_inverse", tent.global_transform.affine_inverse())
			fireflies.material_override.set_shader_parameter("tent_inverse", tent.global_transform.affine_inverse())
			_shelter_ready = true
	var world := Game.world as WorldController
	var daylight := world.daylight() if world != null else 1.0
	var rain := world.weather.rain if world != null and world.weather != null else 0.0
	var wind := world.wind_strength() if world != null else 0.5
	_bird_wind_phase += delta * clampf(wind, 0.0, 1.5)
	var fair := 1.0 - smoothstep(0.10, 0.65, rain)
	motes.emitting = fair > 0.1 and daylight < 0.8
	motes.amount_ratio = fair * (1.0 - smoothstep(0.35, 0.8, daylight))
	_fish_clock += delta * lerpf(0.35, 1.0, daylight) * lerpf(0.8, 1.0, fair)
	_update_small_life(daylight, fair, wind)
	if fireflies != null:
		fireflies.emitting = daylight < 0.45 and fair > 0.1
		fireflies.amount_ratio = clampf(1.0 - daylight * 2.2, 0.0, 1.0) * fair


func apply_quality(p: QualityPreset) -> void:
	_quality_life_scale = p.particle_scale
	if fireflies != null:
		fireflies.amount = maxi(int(round(160.0 * p.particle_scale)), 16)
	if motes != null:
		motes.amount = maxi(int(round(40.0 * p.particle_scale)), 8)


func _build_motes() -> void:
	motes = GPUParticles3D.new()
	motes.name = "Motes"
	motes.amount = 40
	motes.lifetime = 14.0
	motes.preprocess = 10.0
	motes.visibility_aabb = AABB(Vector3(-18, -2, -18), Vector3(36, 10, 36))
	motes.position = Vector3(0.0, 1.4, 0.0)
	var process := ParticleProcessMaterial.new()
	process.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	process.emission_box_extents = Vector3(12.0, 1.6, 12.0)
	process.direction = Vector3(0.2, 0.4, 0.1)
	process.spread = 180.0
	process.initial_velocity_min = 0.02
	process.initial_velocity_max = 0.08
	process.gravity = Vector3(0.0, 0.008, 0.0)
	process.damping_min = 0.2
	process.damping_max = 0.5
	process.scale_min = 0.008
	process.scale_max = 0.02
	process.color = Color(1.0, 0.82, 0.55, 0.7)
	motes.process_material = process
	motes.draw_pass_1 = PropMeshes.sphere_mesh(0.5, 4, 5)
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/mote.gdshader")
	mat.set_shader_parameter("glow", 1.4)
	motes.material_override = mat
	add_child(motes)


func _build_fireflies() -> void:
	fireflies = GPUParticles3D.new()
	fireflies.name = "Fireflies"
	fireflies.amount = 160
	fireflies.lifetime = 11.0
	fireflies.preprocess = 8.0
	fireflies.visibility_aabb = AABB(Vector3(-24, -2, -24), Vector3(48, 12, 48))
	fireflies.position = Vector3(0.0, 1.0, 0.0)
	var process := ParticleProcessMaterial.new()
	process.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	process.emission_box_extents = Vector3(18.0, 1.2, 18.0)
	process.direction = Vector3(0.0, 1.0, 0.0)
	process.spread = 180.0
	process.initial_velocity_min = 0.12
	process.initial_velocity_max = 0.4
	process.gravity = Vector3(0.0, 0.0, 0.0)
	process.damping_min = 0.05
	process.damping_max = 0.2
	process.scale_min = 0.012
	process.scale_max = 0.024
	process.color = Color(0.75, 1.0, 0.35)
	process.hue_variation_min = -0.04
	process.hue_variation_max = 0.06
	# Meandering flight: the turbulence re-aims each firefly as it drifts.
	process.turbulence_enabled = true
	process.turbulence_noise_strength = 1.4
	process.turbulence_noise_scale = 1.3
	process.turbulence_noise_speed = Vector3(0.25, 0.1, 0.25)
	process.turbulence_influence_min = 0.12
	process.turbulence_influence_max = 0.3
	fireflies.process_material = process
	fireflies.draw_pass_1 = PropMeshes.sphere_mesh(0.5, 4, 5)
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/firefly.gdshader")
	mat.set_shader_parameter("glow", 6.0)
	mat.set_shader_parameter("lifetime", fireflies.lifetime)
	fireflies.material_override = mat
	fireflies.emitting = false
	add_child(fireflies)


# All routes and habitat heights are prepared once. Animation has no terrain queries.
func _build_small_life() -> void:
	var bird_mat := ShaderMaterial.new()
	bird_mat.shader = load("res://shaders/small_wildlife.gdshader")
	for i in 3:
		var bird := Node3D.new()
		bird.name = "PondBird%d" % i
		add_child(bird)
		var body := MeshInstance3D.new()
		body.name = "FlyingBody"
		body.mesh = _bird_body()
		body.material_override = bird_mat
		bird.add_child(body)
		for side in [-1.0, 1.0]:
			var wing := MeshInstance3D.new()
			# Twelve millimetres of feather overlap cover wrist yaw/flexion.
			wing.mesh = _bird_wing(side, 0.0, 0.58, Vector3.ZERO)
			wing.material_override = bird_mat
			bird.add_child(wing)
			var wrist := Vector3(side * 0.13, 0.010, 0.008)
			var outer := MeshInstance3D.new()
			outer.name = "Primaries"
			outer.mesh = _bird_wing(side, 0.52, 1.0, wrist)
			outer.position = wrist
			outer.material_override = bird_mat
			wing.add_child(outer)
		var perched := Node3D.new()
		perched.name = "Perched"
		perched.visible = false
		bird.add_child(perched)
		var resting := MeshInstance3D.new()
		resting.mesh = _perched_bird_mesh()
		resting.material_override = bird_mat
		perched.add_child(resting)
		var altitude := 4.5 + i * 0.7
		for step in 81:
			var t := float(step) / 80.0
			var x := -46.0 + t * 24.0
			var z := 1.0 + i * 3.0 + sin(t * PI) * 2.0
			altitude = maxf(altitude, field.height(x, z) + 3.5)
		_bird_heights.append(altitude)
		_birds.append(bird)
	# Small clusters at the meadow vegetation and sheltered east pond margin.
	for at in [Vector2(-15.0, 9.4), Vector2(-10.0, -5.0), MEADOW_INSECTS, Vector2(-44, 18), Vector2(15, 20), Vector2(-52, -3), Vector2(30, -18), Vector2(-19, 24)]:
		_gnat_centres.append(Vector3(at.x, field.height(at.x, at.y) + 0.85, at.y))
	_gnats = MultiMeshInstance3D.new()
	_gnats.name = "VegetationGnats"
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = _gnat_mesh()
	mm.instance_count = 64
	_gnats.multimesh = mm
	_gnats.material_override = bird_mat
	_gnats.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_gnats)
	_build_day_insects(bird_mat)
	var fish_mat := ShaderMaterial.new()
	fish_mat.shader = load("res://shaders/small_fish.gdshader")
	var fish_mesh := _fish_mesh()
	for i in 18:
		var route := PackedVector3Array()
		var school := i / 6
		var member := i % 6
		var centre := Vector2(-28.0 - school * 5.0 - member * 0.32, 5.2 + school * 2.2 + member * 0.22)
		var level := TerrainField.WATER_LEVEL - 0.65 - school * 0.20 - member * 0.06
		var safe := true
		for step in 160:
			var angle := TAU * step / 160.0
			var at := centre + Vector2(cos(angle) * 2.0, sin(angle) * 0.85)
			# Allow for body, tail motion and interpolation between samples.
			if field.height(at.x, at.y) > level - 0.35:
				safe = false
				break
			route.append(Vector3(at.x, level, at.y))
		if not safe:
			continue
		var fish := MeshInstance3D.new()
		fish.name = "PondFish%d" % i
		fish.mesh = fish_mesh
		fish.set_instance_shader_parameter("body_variant", float(i % 3))
		fish.material_override = fish_mat
		fish.set_instance_shader_parameter("swim_phase", i * 1.73)
		fish.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(fish)
		_fish.append(fish)
		fish_routes.append(route)


func _prepare_bird_perch(dock: Dock = null) -> void:
	if dock == null and Game.camp != null and Game.camp.campsite != null:
		dock = Game.camp.campsite.dock
	if dock == null or dock._piles.size() < 5:
		return
	# Third left post: no rope wraps or lantern occupy this cap. Sample the
	# actual mesh, since the driven posts deliberately vary in tilt and height.
	var cap := dock._piles[4]
	var piles := dock.get_node("Piles") as MeshInstance3D
	var hit := piles.mesh.generate_triangle_mesh().intersect_ray(cap + Vector3.UP * 0.2, Vector3.DOWN)
	if hit.is_empty():
		return
	_bird_perch = to_local(dock.to_global(hit.position))
	var normal: Vector3 = global_basis.inverse() * dock.global_basis * hit.normal
	if normal.y < 0.0:
		normal = -normal
	# A side-on resting pose exposes the head/breast from the dock approach.
	# The bird turns toward open water during the first part of its launch.
	var heading := Vector3(-0.40, 0.0, 1.0).normalized()
	_bird_perch_basis = Basis(Quaternion(Vector3.UP, normal.normalized())) * Basis.looking_at(heading)
	_perch_ready = true


func _build_day_insects(material: ShaderMaterial) -> void:
	_butterfly_anchor = Vector3(MEADOW_INSECTS.x, field.height(MEADOW_INSECTS.x, MEADOW_INSECTS.y) + 0.82, MEADOW_INSECTS.y)
	_dragonfly_anchor = Vector3(POND_INSECTS.x, maxf(field.height(POND_INSECTS.x, POND_INSECTS.y) + 0.25, TerrainField.WATER_LEVEL + 0.65), POND_INSECTS.y)
	for i in 12:
		var butterfly := Node3D.new()
		butterfly.name = "MeadowButterfly%d" % i
		add_child(butterfly)
		var body := MeshInstance3D.new()
		body.mesh = _insect_body(false)
		body.material_override = material
		butterfly.add_child(body)
		for side in [-1.0, 1.0]:
			var wing := MeshInstance3D.new()
			wing.mesh = _butterfly_wing(side, i)
			wing.material_override = material
			wing.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			butterfly.add_child(wing)
		_butterflies.append(butterfly)
		var meadow_sites := [Vector2(4.3, 10.2), Vector2(12, 17), Vector2(-10, 19), Vector2(20, -12), Vector2(29, 15), Vector2(-19, -26)]
		var bp: Vector2 = meadow_sites[i / 2]
		_butterfly_habitats.append(Vector3(bp.x, maxf(field.surface_height(bp.x, bp.y), TerrainField.WATER_LEVEL) + 0.82, bp.y))
		var dragonfly := Node3D.new()
		dragonfly.name = "PondDragonfly%d" % i
		add_child(dragonfly)
		var thorax := MeshInstance3D.new()
		thorax.mesh = _insect_body(true)
		thorax.material_override = material
		dragonfly.add_child(thorax)
		for side in [-1.0, 1.0]:
			var wings := MeshInstance3D.new()
			wings.mesh = _dragonfly_wings(side)
			wings.material_override = material
			wings.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			dragonfly.add_child(wings)
		_dragonflies.append(dragonfly)
		var pond_sites := [POND_INSECTS, Vector2(-42, 17), Vector2(-48, -2), Vector2(-32, -10), Vector2(-24, 15), Vector2(-49, 8)]
		var dp: Vector2 = pond_sites[i / 2]
		_dragonfly_habitats.append(Vector3(dp.x, maxf(field.surface_height(dp.x, dp.y) + 0.25, TerrainField.WATER_LEVEL + 0.65), dp.y))


## A small songbird stays on a real perch long enough to read, then travels
## out across open water. No camera-relative scaling or near-screen overlay.
func bird_cue_position(index: int, seconds: float) -> Vector3:
	var start := _bird_perch + Vector3.UP * 0.060
	if index == 0:
		var t := clampf((seconds - BIRD_TAKEOFF) / BIRD_FLIGHT_SECONDS, 0.0, 1.0)
		return start.bezier_interpolate(start + Vector3(-0.9, 0.70, -0.30), start + Vector3(-14.0, 1.7, -6.0), start + Vector3(-25.0, 2.8, -11.0), t)
	var t := clampf((seconds - COMPANION_START) / (COMPANION_END - COMPANION_START), 0.0, 1.0)
	# A later, low crossing occupies reflected water in the dock composition
	# instead of shrinking into the similarly coloured far-bank vegetation.
	return (_bird_perch + Vector3(-6.5, 0.04, 4.1)).bezier_interpolate(_bird_perch + Vector3(-5.3, -0.23, 1.9),
		_bird_perch + Vector3(-3.3, -0.23, -4.1), _bird_perch + Vector3(-2.0, 0.10, -7.6), t)


func _update_film_birds(active: float) -> void:
	for i in _birds.size():
		var bird := _birds[i]
		var started := _film_seconds >= 0.0 if i == 0 else _film_seconds >= COMPANION_START
		var finished := _film_seconds > BIRD_TAKEOFF + BIRD_FLIGHT_SECONDS if i == 0 else _film_seconds > COMPANION_END
		bird.visible = _film_cue == BIRD_CUE and _perch_ready and active > 0.20 and i < 2 and started and not finished
		bird.scale = Vector3.ONE
		if not bird.visible:
			continue
		var perching := i == 0 and _film_seconds < BIRD_TAKEOFF
		for part in 3:
			(bird.get_child(part) as Node3D).visible = not perching
		(bird.get_node("Perched") as Node3D).visible = perching
		if perching:
			bird.position = _bird_perch
			bird.basis = _bird_perch_basis
			continue
		bird.position = bird_cue_position(i, _film_seconds)
		var ahead := bird_cue_position(i, _film_seconds + 0.02)
		var before := bird_cue_position(i, _film_seconds - 0.02)
		var flight_basis := Basis.looking_at((ahead - before).normalized(), Vector3.UP)
		var opening := smoothstep(BIRD_TAKEOFF, BIRD_TAKEOFF + 0.35, _film_seconds) if i == 0 else 1.0
		bird.basis = _bird_perch_basis.slerp(flight_basis, opening) if i == 0 else flight_basis
		(bird.get_child(0) as Node3D).rotation.x = lerpf(0.43, 0.0, opening)
		_animate_bird(bird, i, _film_seconds, 0.0)
		for side_index in 2:
			var wing := bird.get_child(side_index + 1) as Node3D
			var side := -1.0 if side_index == 0 else 1.0
			wing.rotation.z = lerpf(side * 1.30, wing.rotation.z, opening)


func _animate_bird(bird: Node3D, index: int, clock: float, wind_phase: float) -> void:
	# Short, unequal glides interrupt each bird's strokes. The wrist lags
	# the shoulder on recovery, then settles into a shallow gliding dihedral.
	var beat := clock * (31.0 + index * 1.8) + wind_phase
	var cycle := fmod(clock + index * 0.83, 2.7 + index * 0.37)
	var strokes := 1.0 - smoothstep(1.30, 1.58, cycle)
	strokes = maxf(strokes, smoothstep(2.25 + index * 0.3, 2.60 + index * 0.37, cycle))
	var flap := lerpf(0.10, sin(beat) * 0.72, strokes)
	var wrist_flap := sin(beat - 0.72) * 0.25 * strokes
	for side_index in 2:
		var wing := bird.get_child(side_index + 1) as Node3D
		var side := -1.0 if side_index == 0 else 1.0
		wing.rotation.z = side * flap
		var outer := wing.get_child(0) as Node3D
		outer.rotation.z = side * wrist_flap
		outer.rotation.y = side * maxf(0.0, cos(beat)) * 0.14 * strokes


func _update_small_life(daylight: float, fair: float, wind: float) -> void:
	var active := smoothstep(0.12, 0.40, daylight) * fair
	if _film_controlled:
		_update_film_birds(active)
	for i in _birds.size():
		if _film_controlled:
			break
		var bird := _birds[i]
		var t := (_life_clock + i * 13.7) / (36.0 + i * 7.0) * TAU
		bird.visible = active > 0.20
		if not bird.visible:
			continue
		bird.scale = Vector3.ONE
		bird.position = Vector3(-34.0 + cos(t) * 12.0, _bird_heights[i] + 1.2 + sin(t * 2.0) * 0.75, 4.0 + sin(t) * 8.0)
		var tangent := Vector3(-sin(t) * 12.0, cos(t * 2.0) * 1.5, cos(t) * 8.0)
		bird.basis = Basis.looking_at(tangent.normalized(), Vector3.UP)
		bird.rotate_object_local(Vector3.FORWARD, -0.12)
		for part in 3:
			(bird.get_child(part) as Node3D).visible = true
		(bird.get_child(0) as Node3D).rotation.x = 0.0
		(bird.get_node("Perched") as Node3D).visible = false
		_animate_bird(bird, i, _life_clock, _bird_wind_phase)
	if _gnats != null:
		_gnats.visible = active > 0.05
		var clock := _film_seconds if _film_controlled else _life_clock
		for i in _gnats.multimesh.instance_count:
			var t := clock * (1.3 + float(i % 7) * 0.13) + i * 2.399
			var offset := Vector3(sin(t) * 0.30, sin(t * 1.71) * 0.20, cos(t * 1.23) * 0.30)
			offset.x += sin(clock * 0.7) * minf(wind, 1.5) * 0.06
			var basis := Basis(Vector3.UP, t * 0.8)
			_gnats.multimesh.set_instance_transform(i, Transform3D(basis, _gnat_centres[i % _gnat_centres.size()] + offset))
	_update_day_insects(active, wind)
	if not _fish.is_empty():
		(_fish[0].material_override as ShaderMaterial).set_shader_parameter("swim_clock", _fish_clock)
	for i in _fish.size():
		var route := fish_routes[i]
		var cursor := fmod(_fish_clock * (2.4 + float(i / 6) * 0.24) + (i % 6) * 7.0 + (i / 6) * 31.0, float(route.size()))
		var index := int(cursor)
		var next := (index + 1) % route.size()
		var at := route[index].lerp(route[next], cursor - index)
		var tangent_a := (route[next] - route[(index - 1 + route.size()) % route.size()]).normalized()
		var tangent_b := (route[(next + 1) % route.size()] - route[index]).normalized()
		var direction := tangent_a.lerp(tangent_b, cursor - index).normalized()
		_fish[i].position = at
		_fish[i].basis = Basis.looking_at(direction, Vector3.UP)


func _butterfly_position(index: int, seconds: float) -> Vector3:
	var t := seconds * (0.74 + index * 0.09) + index * 2.1
	var anchor := _butterfly_anchor if _film_controlled or _butterfly_habitats.is_empty() else _butterfly_habitats[index]
	var partner := index % 2
	return anchor + Vector3(sin(t) * 0.38 + partner * 0.28,
		sin(t * 1.71) * 0.15 + cos(t * 0.6) * 0.08, cos(t * 0.87) * 0.29 + partner * 0.18)


func _update_day_insects(active: float, wind: float) -> void:
	var clock := _film_seconds if _film_controlled else _life_clock
	for i in _butterflies.size():
		var butterfly := _butterflies[i]
		butterfly.visible = active > 0.25 and wind < 1.8 and (not _film_controlled or (_film_cue == MEADOW_CUE and i < 2)) and i < maxi(2, int(12 * _quality_life_scale))
		if not butterfly.visible:
			continue
		butterfly.position = _butterfly_position(i, clock)
		var tangent := _butterfly_position(i, clock + 0.04) - _butterfly_position(i, clock - 0.04)
		butterfly.basis = Basis.looking_at(tangent.normalized(), Vector3.UP)
		var beat := 0.2 + (sin(clock * (27.0 + i * 2.1) + i) * 0.5 + 0.5) * 1.0
		for side in 2:
			(butterfly.get_child(side + 1) as Node3D).rotation.z = (-1.0 if side == 0 else 1.0) * beat
	for i in _dragonflies.size():
		var dragonfly := _dragonflies[i]
		dragonfly.visible = active > 0.25 and wind < 1.8 and (not _film_controlled or (_film_cue == POND_CUE and i == 0)) and i < maxi(2, int(12 * _quality_life_scale))
		if not dragonfly.visible:
			continue
		var t := clock * 0.73 + i * 2.7
		var dart := smoothstep(-0.40, 0.40, sin(t * 1.2))
		if _film_controlled:
			# Pass through the lily composition. Only one dragonfly uses this
			# close cinematic lane; the second stays out of the film view.
			var progress := clampf(clock / 12.0, 0.0, 1.0)
			var along := lerpf(-1.5, 1.5, progress)
			dragonfly.position = _dragonfly_anchor + Vector3(along, sin(t * 2.1) * 0.045, sin(progress * TAU) * 0.22)
			dragonfly.rotation.y = -PI * 0.5 + cos(progress * TAU) * 0.18
		else:
			var anchor := _dragonfly_habitats[i] if not _dragonfly_habitats.is_empty() else _dragonfly_anchor
			dragonfly.position = anchor + Vector3(lerpf(-0.4, 0.4, dart) + (i % 2) * 0.32,
				sin(t * 2.1) * 0.065 + (i % 2) * 0.13, cos(t * 0.6) * 0.35)
			dragonfly.rotation.y = sin(t * 0.6) * 0.7 + 0.4
		for side in 2:
			(dragonfly.get_child(side + 1) as Node3D).rotation.z = (-1.0 if side == 0 else 1.0) * sin(clock * 91.0 + i * 1.2) * 0.18


static func _perched_bird_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var body := _bird_body().surface_get_arrays(0)
	var tilt := Basis(Vector3.RIGHT, 0.43)
	var lift := Vector3(0.0, 0.067, 0.0)
	var points: PackedVector3Array = body[Mesh.ARRAY_VERTEX]
	var normals: PackedVector3Array = body[Mesh.ARRAY_NORMAL]
	var uv: PackedVector2Array = body[Mesh.ARRAY_TEX_UV]
	var colors: PackedColorArray = body[Mesh.ARRAY_COLOR]
	for i in points.size():
		mb.add_vertex(tilt * points[i] + lift, tilt * normals[i], uv[i], colors[i])
	mb.indices.append_array(body[Mesh.ARRAY_INDEX])
	for side in [-1.0, 1.0]:
		var first := mb.vertex_count()
		# Closed, narrow coverts lie along the flank; flight wings are hidden
		# while resting, rather than sticking out sideways through the post.
		_ellipsoid(mb, Vector3(side * 0.023, 0.002, 0.027), Vector3(0.013, 0.027, 0.072), Color(0.16, 0.17, 0.135), 8, 12)
		for j in range(first, mb.vertex_count()):
			mb.vertices[j] = tilt * mb.vertices[j] + lift
			mb.normals[j] = tilt * mb.normals[j]
		var ankle := Vector3(side * 0.014, 0.003, -0.010)
		mb.add_tube([ankle, Vector3(side * 0.017, 0.034, 0.0), Vector3(side * 0.019, 0.048, 0.012)],
			[0.0015, 0.0020, 0.0025], 5, Color(0.24, 0.19, 0.13), 1.0, 1.0, 0.0, true)
		for toe in 3:
			var end := ankle + Vector3((toe - 1) * 0.007, -0.0023, -0.021 + absf(toe - 1) * 0.004)
			mb.add_tube([ankle, end], [0.0012, 0.00065], 4, Color(0.22, 0.17, 0.12), 1.0, 1.0, 0.0, true)
		mb.add_tube([ankle, ankle + Vector3(0.0, -0.0023, 0.021)], [0.0012, 0.0006], 4, Color(0.22, 0.17, 0.12), 1.0, 1.0, 0.0, true)
	return mb.commit()


static func _gnat_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	_ellipsoid(mb, Vector3.ZERO, Vector3(0.0010, 0.0010, 0.0023), Color(0.09, 0.075, 0.05), 4, 6)
	for side in [-1.0, 1.0]:
		mb.add_quad(Vector3(side * 0.0005, 0.0007, -0.001), Vector3(side * 0.004, 0.0018, -0.0006),
			Vector3(side * 0.0035, 0.0013, 0.0011), Vector3(side * 0.0005, 0.0007, 0.0007), Color(0.38, 0.34, 0.23, 0.25))
	return mb.commit()


static func _insect_body(dragonfly: bool) -> ArrayMesh:
	var mb := MeshBuilder.new()
	var dark := Color(0.055, 0.060, 0.033)
	_ellipsoid(mb, Vector3.ZERO, Vector3(0.0026, 0.0028, 0.006), dark, 5, 8)
	_ellipsoid(mb, Vector3(0, 0.001, -0.007), Vector3(0.0028, 0.0025, 0.0028), dark, 5, 8)
	if dragonfly:
		mb.add_tube([Vector3(0, 0, 0.004), Vector3(0, 0, 0.020), Vector3(0, -0.001, 0.038)],
			[0.0025, 0.0018, 0.00065], 6, Color(0.16, 0.26, 0.27), 1.0, 1.0, 0.0, true)
	else:
		mb.add_tube([Vector3(0, 0, 0.003), Vector3(0, -0.001, 0.012)], [0.0020, 0.0008], 6, dark, 1.0, 1.0, 0.0, true)
		for side in [-1.0, 1.0]:
			mb.add_tube([Vector3(side * 0.001, 0.002, -0.008), Vector3(side * 0.005, 0.005, -0.017)], [0.0003, 0.0004], 4, dark)
	return mb.commit()


static func _butterfly_wing(side: float, variant: int) -> ArrayMesh:
	var mb := MeshBuilder.new()
	# Separate, curved fore/hind lobes avoid the bright nine-sided paper
	# silhouette of the initial mesh. Reflectance stays below ivory canvas.
	var base := Color(0.42, 0.285, 0.12) if variant == 0 else Color(0.48, 0.46, 0.32)
	var fore := PackedVector2Array()
	var hind := PackedVector2Array()
	var curves := [
		[Vector2(0.001, -0.003), Vector2(0.010, -0.020), Vector2(0.017, -0.033), Vector2(0.027, -0.028)],
		[Vector2(0.027, -0.028), Vector2(0.038, -0.023), Vector2(0.036, -0.014), Vector2(0.026, -0.006)],
		[Vector2(0.026, -0.006), Vector2(0.022, 0.001), Vector2(0.009, 0.006), Vector2(0.001, 0.002)],
		[Vector2(0.001, 0.001), Vector2(0.012, -0.002), Vector2(0.026, 0.002), Vector2(0.027, 0.010)],
		[Vector2(0.027, 0.010), Vector2(0.028, 0.023), Vector2(0.018, 0.027), Vector2(0.012, 0.023)],
		[Vector2(0.012, 0.023), Vector2(0.006, 0.019), Vector2(0.003, 0.012), Vector2(0.001, 0.005)],
	]
	for segment in curves.size():
		var curve: Array = curves[segment]
		var start: Vector2 = curve[0]
		for step in 8:
			var point := start.bezier_interpolate(curve[1], curve[2], curve[3], float(step) / 8.0)
			if segment < 3:
				fore.append(point)
			else:
				hind.append(point)
	fore.append(Vector2(0.001, 0.002))
	hind.append(Vector2(0.001, 0.005))
	_butterfly_lobe(mb, fore, Vector2(0.009, -0.009), side, base, 0.0004)
	_butterfly_lobe(mb, hind, Vector2(0.009, 0.009), side, base.darkened(0.09), 0.0)
	# Small scale markings remain dark under direct sun instead of allowing
	# the entire folded wing to wash into a uniform white fleck.
	_ellipsoid(mb, Vector3(side * 0.018, 0.0014, -0.016), Vector3(0.0018, 0.00015, 0.0023), Color(0.10, 0.085, 0.050), 4, 10)
	return mb.commit()


static func _butterfly_lobe(mb: MeshBuilder, outline: PackedVector2Array, centre: Vector2, side: float, base: Color, height: float) -> void:
	const RINGS := 4
	var ci := mb.add_vertex(Vector3(centre.x * side, height + 0.0009, centre.y), Vector3.UP, Vector2.ZERO, base.darkened(0.14))
	var start := mb.vertex_count()
	for ring in range(1, RINGS + 1):
		var r := float(ring) / RINGS
		for i in outline.size():
			var p := centre.lerp(outline[i], r)
			var direction := (p - centre).normalized()
			var border := smoothstep(0.80, 1.0, r) * 0.36
			var vein := pow(maxf(cos(float(i) / outline.size() * TAU * 7.0), 0.0), 18.0) * 0.13
			var tint := base.darkened(border + vein + (1.0 - r) * 0.07)
			var normal := Vector3(direction.x * side * 0.08 * r, 1.0, direction.y * 0.08 * r).normalized()
			mb.add_vertex(Vector3(p.x * side, height + (1.0 - r * r) * 0.0009, p.y), normal, Vector2(r, float(i) / outline.size()), tint)
	for i in outline.size():
		var next := (i + 1) % outline.size()
		if side < 0.0:
			mb.add_triangle(ci, start + i, start + next)
		else:
			mb.add_triangle(ci, start + next, start + i)
		for ring in RINGS - 1:
			var a := start + ring * outline.size() + i
			var b := start + ring * outline.size() + next
			mb.add_quad_facing(a, a + outline.size(), b + outline.size(), b, Vector3.UP)


static func _dragonfly_wings(side: float) -> ArrayMesh:
	var mb := MeshBuilder.new()
	for pair in 2:
		var z := pair * 0.007 - 0.003
		mb.add_quad(Vector3(side * 0.002, 0.001, z), Vector3(side * 0.037, 0.002, z - 0.009),
			Vector3(side * 0.040, 0.001, z - 0.004), Vector3(side * 0.007, 0.001, z + 0.003), Color(0.46, 0.49, 0.39, 0.25))
	return mb.commit()


# Analytic ellipsoid normals share the wrap seam exactly; never recompute them
# after joining eyes, head or fins to the body.
static func _ellipsoid(mb: MeshBuilder, centre: Vector3, radii: Vector3, color: Color,
		rings := 10, sectors := 16) -> void:
	var start := mb.vertex_count()
	for row in range(rings + 1):
		var phi := PI * row / float(rings)
		for col in sectors:
			var theta := TAU * col / float(sectors)
			var dir := Vector3(sin(phi) * cos(theta), cos(phi), sin(phi) * sin(theta))
			mb.add_vertex(centre + dir * radii, (dir / radii).normalized(), Vector2(float(col) / sectors, float(row) / rings), color)
	# Pole quads have coincident first vertices: their zero-area winding
	# test cannot select a front face. Emit explicit clockwise fans instead.
	for col in sectors:
		var next := (col + 1) % sectors
		mb.add_triangle(start + col, start + sectors + col, start + sectors + next)
		mb.add_triangle(start + rings * sectors + col,
			start + (rings - 1) * sectors + next, start + (rings - 1) * sectors + col)
	for row in range(1, rings - 1):
		for col in sectors:
			var a := start + row * sectors + col
			var b := start + row * sectors + (col + 1) % sectors
			var outward := (mb.vertices[a] + mb.vertices[b]) * 0.5 - centre
			mb.add_quad_facing(a, b, b + sectors, a + sectors, outward)


static func _bird_body() -> ArrayMesh:
	var mb := MeshBuilder.new()
	_ellipsoid(mb, Vector3(0, 0, 0.002), Vector3(0.029, 0.029, 0.076), Color(0.12, 0.125, 0.10))
	_ellipsoid(mb, Vector3(0, 0.017, -0.062), Vector3(0.023, 0.025, 0.030), Color(0.10, 0.105, 0.085))
	# Small breast and cheek tones survive silhouette-distance lighting.
	_ellipsoid(mb, Vector3(0, -0.011, -0.020), Vector3(0.025, 0.022, 0.050), Color(0.38, 0.355, 0.28))
	mb.add_tube([Vector3(0, 0.015, -0.083), Vector3(0, 0.011, -0.110)], [0.007, 0.0006], 8, Color(0.055, 0.052, 0.04))
	for side in [-1.0, 1.0]:
		_ellipsoid(mb, Vector3(side * 0.020, 0.022, -0.073), Vector3(0.003, 0.003, 0.003), Color(0.008, 0.009, 0.007), 6, 8)
	# Two tapered tail lobes with an actual shallow central cleft.
	var start := mb.vertex_count()
	for row in 5:
		var t := row / 4.0
		for col in 13:
			var u := col / 6.0 - 1.0
			var edge := 0.107 + absf(u) * 0.031
			var pos := Vector3(u * lerpf(0.010, 0.026, t), -0.002 - t * 0.006, lerpf(0.055, edge, t))
			mb.add_vertex(pos, Vector3.UP, Vector2(col / 12.0, t), Color(0.08, 0.085, 0.065))
	for row in 4:
		for col in 12:
			var a := start + row * 13 + col
			mb.add_quad_facing(a, a + 1, a + 14, a + 13, Vector3.UP)
	return mb.commit()


static func _bird_wing(side: float, span_start: float, span_end: float, origin: Vector3) -> ArrayMesh:
	var mb := MeshBuilder.new()
	const ROWS := 12
	const COLS := 5
	for row in range(ROWS + 1):
		var t := lerpf(span_start, span_end, row / float(ROWS))
		var x := side * (0.020 + t * 0.20)
		var leading := -0.040 - sin(t * PI) * 0.025 + t * t * 0.140
		var chord := (0.085 + sin(t * PI) * 0.023) * pow(1.0 - t, 0.65) + 0.001
		for col in range(COLS + 1):
			var u := col / float(COLS)
			var feather_edge := sin(t * PI * 13.0) * sin(t * PI * 13.0) * 0.002 * u * u * smoothstep(0.35, 0.75, t)
			var pos := Vector3(x, sin(t * PI) * 0.010 + sin(u * PI) * 0.003, leading + chord * u - feather_edge)
			if span_start > 0.0:
				# Layer the proximal primaries just under the coverts while
				# gliding, avoiding coplanar flicker in the overlapping strip.
				pos.y -= 0.0008 * (1.0 - smoothstep(0.52, 0.61, t))
			var tone := Color(0.13, 0.135, 0.105).lerp(Color(0.065, 0.070, 0.052), smoothstep(0.35, 1.0, t) * 0.7 + u * 0.2)
			mb.add_vertex(pos - origin, Vector3.UP, Vector2(t, u), tone)
	for row in ROWS:
		for col in COLS:
			var a := row * (COLS + 1) + col
			mb.add_quad_facing(a, a + 1, a + COLS + 2, a + COLS + 1, Vector3.UP)
	mb.recompute_normals()
	return mb.commit()


static func _fish_section(t: float) -> Vector3:
	var belly := pow(maxf(sin(t * PI), 0.0), 0.82)
	var taper := 1.0 - smoothstep(0.38, 0.92, t) * 0.79
	return Vector3(-0.095 + t * 0.165, 0.001 + belly * 0.018 * taper + smoothstep(0.78, 1.0, t) * 0.002,
		0.001 + belly * 0.029 * taper + smoothstep(0.78, 1.0, t) * 0.004)


static func _fish_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	const RINGS := 40
	const SECTORS := 20
	# One continuous longitudinal surface with derivative normals: no split
	# longitude vertices or separately shaded halves at the belly.
	for row in range(RINGS + 1):
		var t := row / float(RINGS)
		var section := _fish_section(t)
		var before := _fish_section(maxf(0.0, t - 0.001))
		var after := _fish_section(minf(1.0, t + 0.001))
		var derivative := (after - before) / maxf(after.x - before.x, 0.00001)
		for col in SECTORS:
			var a := TAU * col / float(SECTORS)
			var n := Vector3(cos(a) / section.y, sin(a) / section.z,
				-derivative.y * cos(a) * cos(a) / section.y - derivative.z * sin(a) * sin(a) / section.z).normalized()
			mb.add_vertex(Vector3(section.y * cos(a), section.z * sin(a), section.x), n, Vector2(col / float(SECTORS), t))
	for row in RINGS:
		for col in SECTORS:
			var a := row * SECTORS + col
			var b := row * SECTORS + (col + 1) % SECTORS
			mb.add_quad_facing(a, b, b + SECTORS, a + SECTORS, Vector3(mb.vertices[a].x, mb.vertices[a].y, 0))
	# Close the tiny mouth and peduncle ends without splitting body normals.
	for end in 2:
		var section := _fish_section(float(end))
		var outward := Vector3.FORWARD if end == 0 else Vector3.BACK
		var centre := mb.add_vertex(Vector3(0, 0, section.x), outward, Vector2.ZERO)
		for col in SECTORS:
			var a := end * RINGS * SECTORS + col
			var b := end * RINGS * SECTORS + (col + 1) % SECTORS
			if end == 0:
				mb.add_triangle(centre, a, b)
			else:
				mb.add_triangle(centre, b, a)
	# Forked caudal fin grows from the narrow peduncle, with swept lobes.
	var start := mb.vertex_count()
	for row in 7:
		var t := row / 6.0
		for col in 17:
			var u := col / 8.0 - 1.0
			var z := lerpf(0.066, 0.090 + pow(absf(u), 0.65) * 0.025, t)
			var y := u * lerpf(0.005, 0.029, t)
			mb.add_vertex(Vector3(sin(u * PI) * t * 0.001, y, z), Vector3.RIGHT, Vector2(col / 16.0, t), Color(0.72, 0.73, 0.57, 0.5))
	for row in 6:
		for col in 16:
			var a := start + row * 17 + col
			mb.add_quad_facing(a, a + 1, a + 18, a + 17, Vector3.RIGHT)
	# Low rounded dorsal/anal fins and paired pectoral/pelvic fins.
	_fin(mb, Vector3(0, 0.022, -0.022), Vector3(0, 0.010, 0.044), Vector3(0, 0.043, -0.006))
	_fin(mb, Vector3(0, -0.018, 0.012), Vector3(0, -0.009, 0.050), Vector3(0, -0.032, 0.026))
	for side in [-1.0, 1.0]:
		_fin(mb, Vector3(side * 0.014, -0.011, -0.049), Vector3(side * 0.011, -0.013, -0.027), Vector3(side * 0.034, -0.021, -0.014))
		_fin(mb, Vector3(side * 0.009, -0.016, 0.015), Vector3(side * 0.008, -0.009, 0.037), Vector3(side * 0.022, -0.024, 0.037))
		# Tiny olive iris and black pupil are real geometry, not a pasted disc.
		_ellipsoid(mb, Vector3(side * 0.0117, 0.007, -0.073), Vector3(0.0027, 0.0034, 0.0034), Color(0.32, 0.29, 0.12, 0.0), 6, 10)
		_ellipsoid(mb, Vector3(side * 0.0139, 0.007, -0.073), Vector3(0.0012, 0.0022, 0.0022), Color(0.008, 0.011, 0.008, 0.0), 6, 10)
	return mb.commit()


static func _fin(mb: MeshBuilder, a: Vector3, b: Vector3, tip: Vector3) -> void:
	var start := mb.vertex_count()
	var n := (b - a).cross(tip - a).normalized()
	for row in 5:
		var t := row / 4.0
		for col in 9:
			var u := col / 8.0
			var root := a.lerp(b, u)
			var edge := a * ((1.0 - u) * (1.0 - u)) + tip * (2.0 * u * (1.0 - u)) + b * (u * u)
			mb.add_vertex(root.lerp(edge, t), n, Vector2(u, t), Color(0.72, 0.73, 0.57, 0.5))
	for row in 4:
		for col in 8:
			var v := start + row * 9 + col
			mb.add_quad_facing(v, v + 1, v + 10, v + 9, n)
