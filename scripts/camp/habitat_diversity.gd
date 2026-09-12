class_name HabitatDiversity
extends Node3D

## Real-scale plant communities, not extra copies of the lawn blade. All roots
## sample the shared surface. Small spatial cells keep off-screen work cullable.
enum Form { ROSETTE, FERNLET, RUSH, LITTER }
const CELL_SIZE := 16.0
const EXTENT := 92.0
const ATTEMPTS := 18500
const TARGETS := [1550, 1450, 1150, 2600]
const SHADER := preload("res://shaders/habitat.gdshader")
var counts := PackedInt32Array([0, 0, 0, 0])
var batches: Array[MultiMeshInstance3D] = []
var material: ShaderMaterial
var _clock := 0.0
var _built := false


func setup(camp: Camp) -> void:
	if _built:
		return
	_built = true
	name = "HabitatDiversity"
	var groups := plan(camp.field)
	var meshes: Array[ArrayMesh] = []
	for form in 4:
		for variant in 2:
			meshes.append(plant_mesh(form, 60011 + form * 113 + variant * 31))
	material = ShaderMaterial.new()
	material.shader = SHADER
	for key: Vector4i in groups:
		var items: Array = groups[key]
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.use_custom_data = true
		mm.mesh = meshes[key.z * 2 + key.w]
		mm.instance_count = items.size()
		var bounds := AABB()
		for i in items.size():
			var item: Dictionary = items[i]
			var frame: Transform3D = item.frame
			mm.set_instance_transform(i, frame)
			mm.set_instance_custom_data(i, item.custom)
			var local := frame * mm.mesh.get_aabb().grow(0.14)
			bounds = local if i == 0 else bounds.merge(local)
		mm.custom_aabb = bounds
		var node := MultiMeshInstance3D.new()
		node.name = "Habitat_%d_%d_%d_%d" % [key.x, key.y, key.z, key.w]
		node.multimesh = mm
		node.material_override = material
		node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		node.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
		# Keep broad wet-bank leaves in the mirror; small forest-floor details
		# use the existing cheap grass layer, not every reflection viewport.
		node.layers = 1 if key.z == Form.RUSH else Pond.GRASS_LAYER
		node.visibility_range_end_margin = 4.0
		add_child(node)
		batches.append(node)
		counts[key.z] += items.size()
	Quality.preset_changed.connect(_quality)
	_quality(Quality.current)
	print("SHOWCASE_HABITAT rosettes=%d fernlets=%d rushes=%d litter=%d batches=%d" % [counts[0], counts[1], counts[2], counts[3], batches.size()])


static func placement_allowed(field: TerrainField, p: Vector2, radius: float) -> bool:
	if field.walking_distance(p) < 0.70 + radius or TerrainField.camp_wear(p) > 0.05:
		return false
	if p.distance_to(TerrainField.DOCK_START) < 4.0 + radius:
		return false
	var lane: Array[Vector2] = [TerrainField.DOCK_START, TerrainField.POND_CENTRE]
	if TerrainField.distance_to_polyline(p, lane) < 2.8 + radius:
		return false
	return field.surface_slope(p.x, p.y) < 0.63


static func plan(field: TerrainField, attempts := ATTEMPTS) -> Dictionary:
	var rng := RandomNumberGenerator.new()
	rng.seed = 811357
	var patches := FastNoiseLite.new()
	patches.seed = 7347
	patches.frequency = 0.16
	var accepted := PackedInt32Array([0, 0, 0, 0])
	var groups := {}
	for i in attempts:
		var angle := rng.randf() * TAU
		var p: Vector2
		if i % 3 == 0:
			var shore := TerrainField.shore_point(angle)
			p = shore + (shore - TerrainField.POND_CENTRE).normalized() * rng.randf_range(-0.18, 6.0)
		else:
			p = Vector2(cos(angle), sin(angle)) * sqrt(rng.randf()) * EXTENT
		var width := rng.randf_range(0.60, 1.3)
		if not placement_allowed(field, p, 0.48 * width):
			continue
		var y := field.surface_height(p.x, p.y)
		var above := y - TerrainField.WATER_LEVEL
		if above < -0.09:
			continue
		var shade := field.woodland_cover(p.x, p.y)
		var colony := smoothstep(-0.4, 0.38, patches.get_noise_2d(p.x, p.y))
		if rng.randf() > colony * 0.82:
			continue
		var form := Form.ROSETTE
		if above < 0.34:
			form = Form.RUSH
		elif shade > 0.42:
			form = Form.FERNLET if rng.randf() < 0.46 else Form.LITTER
		elif rng.randf() > 0.32:
			continue
		if accepted[form] >= TARGETS[form]:
			continue
		var yaw := rng.randf() * TAU
		var normal := field.normal(p.x, p.y)
		# Tilt only low litter to the terrain; live plants grow against gravity.
		var basis := Basis(Quaternion(Vector3.UP, normal)) if form == Form.LITTER else Basis.IDENTITY
		basis = basis * Basis(Vector3.UP, yaw) * Basis.from_scale(Vector3(width, rng.randf_range(0.68, 1.20), width))
		var origin := Vector3(p.x, y + (0.009 if form == Form.LITTER else -0.012), p.y)
		var key := Vector4i(floori(p.x / CELL_SIZE), floori(p.y / CELL_SIZE), form, rng.randi() % 2)
		if not groups.has(key):
			groups[key] = []
		var tint := rng.randf_range(0.83, 1.12)
		groups[key].append({frame = Transform3D(basis, origin), custom = Color(rng.randf(), tint, tint * rng.randf_range(0.95, 1.05), tint * rng.randf_range(0.83, 1.0))})
		accepted[form] += 1
	return groups


## Geometry uses continuous curled leaves and tapered stems. Leaf colour is
## stored per vertex, so no new external atlas or texture import is required.
static func plant_mesh(form: int, seed_value: int) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var mb := MeshBuilder.new()
	match form:
		Form.ROSETTE:
			for i in 7:
				var a := i * 2.39996 + rng.randf_range(-0.20, 0.20)
				var out := Vector3(cos(a), 0.0, sin(a))
				var h := rng.randf_range(0.04, 0.13)
				var at := out * 0.05 + Vector3.UP * h
				mb.add_tube([Vector3.ZERO, at], [0.002, 0.001], 4, Color(0.09, 0.18, 0.055))
				_leaf(mb, at, out, rng.randf_range(0.13, 0.23), 0.055, 0.065, Color(0.12, 0.24, 0.065), false)
		Form.FERNLET:
			for frond in 6:
				var a := frond * 2.39996
				var out := Vector3(cos(a), 0, sin(a))
				var reach := rng.randf_range(0.20, 0.39)
				var points: Array[Vector3] = []
				var radii: Array[float] = []
				for j in 15:
					var t := j / 14.0
					var at := out * t * reach + Vector3.UP * (0.018 + sin(t * PI * 0.76) * 0.17)
					points.append(at)
					radii.append(0.002 * (1.0 - t * 0.70))
					if j > 0:
						for side in [-1.0, 1.0]:
							_leaf(mb, at, out.rotated(Vector3.UP, side * 1.05), 0.087 * pow(1.0 - t * 0.88, 0.8), 0.014, 0.008, Color(0.08, 0.22, 0.055), false)
				mb.add_tube(points, radii, 4, Color(0.10, 0.19, 0.06))
		Form.RUSH:
			for i in 11:
				var a := i * 2.39996
				var out := Vector3(cos(a), 0, sin(a))
				var h := rng.randf_range(0.38, 0.82)
				var bend := rng.randf_range(0.06, 0.17)
				var points: Array[Vector3] = []
				var radii: Array[float] = []
				for j in 6:
					var t := j / 5.0
					points.append(out * (0.022 + bend * t * t) + Vector3.UP * h * t)
					radii.append(lerpf(0.0038, 0.0005, t))
				mb.add_tube(points, radii, 5, Color(0.15, 0.24, 0.085))
				if i % 3 == 0:
					var tip := points[4]
					for seed in 4:
						var at := tip + out.rotated(Vector3.UP, seed * 1.7) * 0.014 + Vector3.UP * seed * 0.003
						MeadowPlants._flower_core(mb, at, 0.005, 0.85, Color(0.24, 0.15, 0.05))
		Form.LITTER:
			for i in 5:
				var a := rng.randf() * TAU
				var out := Vector3(cos(a), 0, sin(a))
				var at := out * rng.randf_range(0.0, 0.16) + Vector3.UP * rng.randf_range(0.004, 0.014)
				_leaf(mb, at, out, rng.randf_range(0.07, 0.14), 0.037, 0.008, Color(0.23, 0.115, 0.045).lerp(Color(0.39, 0.24, 0.08), rng.randf()), true)
	mb.recompute_normals()
	mb.recompute_tangents()
	return mb.commit(null, true)


static func _leaf(mb: MeshBuilder, at: Vector3, out: Vector3, length: float, width: float, lift: float, tint: Color, lobed: bool) -> void:
	var start := mb.vertex_count()
	var side := Vector3(-out.z, 0, out.x)
	for row in 9:
		var t := row / 8.0
		var w := maxf(sin(t * PI), 0.01) * width
		if lobed:
			w *= 0.78 + 0.22 * cos(t * PI * 8.0)
		for col in 3:
			var u := float(col) - 1.0
			var p := at + out * t * length + side * u * w + Vector3.UP * (sin(t * PI) * lift + u * u * sin(t * PI) * width * 0.16)
			mb.add_vertex(p, Vector3.UP, Vector2(col / 2.0, t), tint * (0.90 if col == 1 else 1.0))
	for row in 8:
		for col in 2:
			var a := start + row * 3 + col
			mb.add_quad_facing(a, a + 1, a + 4, a + 3, Vector3.UP)


func _process(delta: float) -> void:
	_clock += delta
	if material != null:
		material.set_shader_parameter("habitat_clock", _clock)


func _quality(p: QualityPreset) -> void:
	material.set_shader_parameter("fade_start", 85.0 * p.foliage_distance)
	material.set_shader_parameter("fade_end", 140.0 * p.foliage_distance)
	# Range-cull each cell once its plants have faded out (range is measured to
	# the bounds' centre, so add half the cell bounds' diagonal).
	for node in batches:
		node.visibility_range_end = 140.0 * p.foliage_distance + node.multimesh.custom_aabb.size.length() * 0.5 + 2.0


func _exit_tree() -> void:
	if Quality.preset_changed.is_connected(_quality):
		Quality.preset_changed.disconnect(_quality)
