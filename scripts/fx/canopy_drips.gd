class_name CanopyDrips
extends Node3D

## Secondary water shedding from the generated tree crowns after rain. Drops
## are placed deterministically from the actual procedural crown bounds and end
## on the same TerrainField/water level used by the rest of the world. The GPU
## animates the fall; no per-drop physics or raycasts run during gameplay.

const DRIP_SHADER := preload("res://shaders/canopy_drip.gdshader")
const MAX_DROPS := 3200
const MAX_TREE_RADIUS := 104.0
const FALL_RANGE := 32.0

var _camp: Camp
var _world: WorldController
var _mesh: MultiMeshInstance3D
var _material: ShaderMaterial
var _built := false
var _clock := 0.0
var _strength := 0.0


func _ready() -> void:
	name = "CanopyDrips"
	set_process(false)


static func post_rain_strength(wetness: float, rainfall: float) -> float:
	var wet := clampf(wetness, 0.0, 1.0)
	var rain_suppression := 1.0 - smoothstep(0.05, 0.62, clampf(rainfall, 0.0, 1.0))
	return wet * rain_suppression


func _process(delta: float) -> void:
	if not _built:
		return
	if not is_instance_valid(_world) or _world.weather == null:
		return
	_clock += delta
	var target := post_rain_strength(_world.weather.wet, _world.weather.rain)
	# Canopy shedding rises promptly when rain stops but decays with retained
	# scene wetness rather than switching off as one synchronized event.
	var rate := 2.8 if target > _strength else 0.75
	_strength = lerpf(_strength, target, 1.0 - exp(-delta * rate))
	if _material != null:
		_material.set_shader_parameter("drip_clock", _clock)
		_material.set_shader_parameter("drip_strength", _strength)
	if _mesh != null:
		_mesh.visible = _strength > 0.008


func setup(camp: Camp, world: WorldController) -> void:
	if _built:
		return
	_camp = camp
	_world = world
	_build()
	_built = true
	set_process(true)


func _build() -> void:
	var transforms: Array[Transform3D] = []
	var customs: Array[Color] = []
	var rng := RandomNumberGenerator.new()
	rng.seed = 420917
	for entry in _camp.plan.near_trees():
		if transforms.size() >= MAX_DROPS:
			break
		if entry.position.length() > MAX_TREE_RADIUS:
			continue
		var species := TreeSpecies.by_kind(entry.kind)
		if not species.has_leaves():
			continue
		var variants: Array = _camp.forest.variants[entry.kind]
		if variants.is_empty():
			continue
		var result: TreeGenerator.Result = variants[absi(entry.seed_value) % variants.size()]
		var scale := entry.scale
		var base_y := _camp.field.surface_height(entry.position.x, entry.position.y) - 0.12 * scale
		var crown_radius := result.crown_radius * scale
		var crown_y := base_y + result.crown_center.y * scale
		var offset := Basis(Vector3.UP, entry.rotation) * result.crown_center * scale
		var crown_xz := entry.position + Vector2(offset.x, offset.z)
		# Mature crowns shed more drops, while small saplings remain a sparse
		# accent. Count is capped globally and later quality-gated in the shader.
		var count := clampi(int(round(crown_radius * 2.2)), 4, 14)
		for i in count:
			if transforms.size() >= MAX_DROPS:
				break
			var angle := rng.randf() * TAU
			var radius := sqrt(rng.randf()) * crown_radius * rng.randf_range(0.35, 0.92)
			var xz := crown_xz + Vector2(cos(angle), sin(angle)) * radius
			# Outer branches are lower; random crown depth prevents one horizontal
			# plane of droplets from betraying the procedural placement.
			var radial := radius / maxf(crown_radius, 0.01)
			var source_y := crown_y + crown_radius * rng.randf_range(-0.20, 0.34)
			source_y -= crown_radius * radial * radial * rng.randf_range(0.08, 0.26)
			var ground_y := _camp.field.height_fast(xz.x, xz.y)
			if absf(xz.x) > TerrainBuilder.INNER_UNIFORM_HALF or absf(xz.y) > TerrainBuilder.INNER_UNIFORM_HALF:
				ground_y = _camp.field.surface_height(xz.x, xz.y)
			ground_y = maxf(ground_y, TerrainField.WATER_LEVEL + 0.006)
			var fall_height := clampf(source_y - ground_y, 0.35, FALL_RANGE)
			if fall_height <= 0.36:
				continue
			transforms.append(Transform3D(Basis.IDENTITY, Vector3(xz.x, source_y, xz.y)))
			customs.append(Color(fall_height / FALL_RANGE, rng.randf(), rng.randf(), rng.randf()))

	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	mm.mesh = quad
	mm.instance_count = transforms.size()
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
		mm.set_instance_custom_data(i, customs[i])
	_mesh = MultiMeshInstance3D.new()
	_mesh.name = "CanopyDripField"
	_mesh.multimesh = mm
	_material = ShaderMaterial.new()
	_material.shader = DRIP_SHADER
	_material.set_shader_parameter("fall_range", FALL_RANGE)
	# Keep drops out of shelter volumes. These are the same built prop frames
	# used by the rain system, not guessed world-space rectangles.
	_material.set_shader_parameter("tent_inverse", camp_tent_inverse())
	_material.set_shader_parameter("dock_inverse", _camp.campsite.dock.global_transform.affine_inverse())
	_material.set_shader_parameter("dock_length", _camp.campsite.dock.total_length())
	_material.set_shader_parameter("density_scale", clampf(Quality.current.particle_scale, 0.0, 1.0))
	_mesh.material_override = _material
	_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_mesh.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	_mesh.custom_aabb = AABB(Vector3(-MAX_TREE_RADIUS - 20.0, TerrainField.WATER_LEVEL - 0.2, -MAX_TREE_RADIUS - 20.0),
		Vector3((MAX_TREE_RADIUS + 20.0) * 2.0, 48.0, (MAX_TREE_RADIUS + 20.0) * 2.0))
	_mesh.visible = false
	add_child(_mesh)
	Quality.preset_changed.connect(_apply_quality)
	print("Canopy drips: %d deterministic crown samples" % transforms.size())


func _apply_quality(preset: QualityPreset) -> void:
	if _material != null:
		_material.set_shader_parameter("density_scale", clampf(preset.particle_scale, 0.0, 1.0))


func _exit_tree() -> void:
	if Quality.preset_changed.is_connected(_apply_quality):
		Quality.preset_changed.disconnect(_apply_quality)


func camp_tent_inverse() -> Transform3D:
	return _camp.campsite.tent.global_transform.affine_inverse()
