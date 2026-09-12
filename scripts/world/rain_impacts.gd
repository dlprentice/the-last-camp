class_name RainImpacts
extends Node3D
## Fixed contact samples on the actual uppermost camp meshes. The GPU animates
## small ballistic droplets; terrain/triangle queries happen only at build.

const CELL := 2.0
var _triangles: Array[PackedVector3Array] = []
var _cells: Dictionary = {}
var _field: TerrainField
var _material: ShaderMaterial
var sample_count := 0


func build(camp: Camp) -> void:
	name = "RainImpacts"
	_field = camp.field
	_collect(camp.campsite)
	_collect(camp.rocks)
	var rng := RandomNumberGenerator.new()
	rng.seed = 821553
	var samples: Array[Vector3] = []
	var normals: Array[Vector3] = []
	for z in range(-22, 31):
		for x in range(-54, 20):
			var p := Vector2(x + rng.randf(), z + rng.randf())
			# Dense grass conceals small ground impacts; spend them at the pond,
			# along paths and the clearing rather than above the blade canopy.
			if _field.grass_suitability(p.x, p.y) > 0.40 and rng.randf() > 0.16:
				continue
			_sample(p, samples, normals)
	# Close prop surfaces merit denser droplets than the distant terrain.
	var dock := camp.campsite.dock
	for i in 460:
		var p := dock.to_global(Vector3(rng.randf_range(-0.77, 0.77), 0,
			rng.randf_range(-dock.total_length(), 0)))
		_sample(Vector2(p.x, p.z), samples, normals)
	var tent := camp.campsite.tent
	for i in 240:
		var p := tent.to_global(Vector3(rng.randf_range(-1.18, 1.18), 0, rng.randf_range(-1.48, 1.48)))
		_sample(Vector2(p.x, p.z), samples, normals)
	for rock in camp.plan.rocks:
		if rock.scale < 0.6 or rock.position.length() > 56.0:
			continue
		for i in mini(32, int(rock.scale * rock.scale * 8.0)):
			var p := rock.position + Vector2(rng.randf_range(-0.42, 0.42), rng.randf_range(-0.42, 0.42)) * rock.scale
			_sample(p, samples, normals)
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.mesh = _droplets()
	mm.instance_count = samples.size()
	for i in samples.size():
		var normal := normals[i]
		var tangent := normal.cross(Vector3.FORWARD).normalized()
		if tangent.length_squared() < 0.01:
			tangent = Vector3.RIGHT
		var basis := Basis(tangent, normal, tangent.cross(normal)).rotated(normal, rng.randf() * TAU)
		mm.set_instance_transform(i, Transform3D(basis, samples[i] + normal * 0.008))
		var water_contact := is_equal_approx(samples[i].y, TerrainField.WATER_LEVEL)
		mm.set_instance_custom_data(i, Color(rng.randf(), rng.randf(), 1.0 if water_contact else 0.0, rng.randf()))
	_material = ShaderMaterial.new()
	_material.shader = load("res://shaders/rain_impact.gdshader")
	var mesh := MultiMeshInstance3D.new()
	mesh.multimesh = mm
	mesh.material_override = _material
	mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mesh.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	mesh.extra_cull_margin = 0.3
	add_child(mesh)
	sample_count = samples.size()
	_triangles.clear()
	_cells.clear()
	print("RAIN_IMPACTS contacts=", sample_count)


func update(clock: float, amount: float) -> void:
	visible = amount > 0.001
	if _material != null:
		_material.set_shader_parameter("impact_clock", clock)


func _collect(node: Node) -> void:
	# This cache is for fixed surfaces. The canoe bobs and rolls; retaining its
	# construction pose would leave small splash droplets hanging in space.
	if node is Canoe:
		return
	if node is MeshInstance3D and node.mesh != null:
		var material: Material = node.material_override
		var include_mesh := material is StandardMaterial3D or material == null
		if material is ShaderMaterial:
			include_mesh = material.shader.resource_path.get_file() in ["prop.gdshader", "wood_uv.gdshader", "canvas.gdshader"]
		if include_mesh:
			var faces: PackedVector3Array = node.mesh.get_faces()
			for i in range(0, faces.size(), 3):
				var a: Vector3 = node.global_transform * faces[i]
				var b: Vector3 = node.global_transform * faces[i + 1]
				var c: Vector3 = node.global_transform * faces[i + 2]
				var n := (b - a).cross(c - a).normalized()
				if absf(n.y) < 0.20:
					continue
				var idx := _triangles.size()
				_triangles.append(PackedVector3Array([a, b, c]))
				for z in range(int(floor(minf(a.z, minf(b.z, c.z)) / CELL)), int(floor(maxf(a.z, maxf(b.z, c.z)) / CELL)) + 1):
					for x in range(int(floor(minf(a.x, minf(b.x, c.x)) / CELL)), int(floor(maxf(a.x, maxf(b.x, c.x)) / CELL)) + 1):
						var key := Vector2i(x, z)
						if not _cells.has(key):
							_cells[key] = []
						_cells[key].append(idx)
	for child in node.get_children():
		_collect(child)


func _sample(p: Vector2, samples: Array[Vector3], normals: Array[Vector3]) -> void:
	var ground := _field.height(p.x, p.y)
	var height := maxf(ground, TerrainField.WATER_LEVEL)
	var normal := Vector3.UP
	if ground > TerrainField.WATER_LEVEL:
		normal = Vector3(_field.height(p.x - 0.05, p.y) - _field.height(p.x + 0.05, p.y),
			0.1, _field.height(p.x, p.y - 0.05) - _field.height(p.x, p.y + 0.05)).normalized()
	var key := Vector2i(floori(p.x / CELL), floori(p.y / CELL))
	for idx in _cells.get(key, []):
		var tri := _triangles[idx]
		var a := Vector2(tri[0].x, tri[0].z)
		var b := Vector2(tri[1].x, tri[1].z)
		var c := Vector2(tri[2].x, tri[2].z)
		var denom := (b - a).cross(c - a)
		if absf(denom) < 0.000001:
			continue
		var u := (p - a).cross(c - a) / denom
		var v := (b - a).cross(p - a) / denom
		if u < 0 or v < 0 or u + v > 1:
			continue
		var y := tri[0].y + u * (tri[1].y - tri[0].y) + v * (tri[2].y - tri[0].y)
		if y > height:
			height = y
			normal = (tri[1] - tri[0]).cross(tri[2] - tri[0]).normalized()
			normal *= signf(normal.y)
	samples.append(Vector3(p.x, height, p.y))
	normals.append(normal)


static func _droplets() -> ArrayMesh:
	var mb := MeshBuilder.new()
	for i in 6:
		# Red encodes azimuth, green vertical launch speed, blue size.
		var data := Color(float(i) / 6.0, fmod(i * 0.618, 1.0), fmod(i * 0.37, 1.0), 1)
		mb.add_quad(Vector3(-0.5, -0.5, 0), Vector3(0.5, -0.5, 0),
			Vector3(0.5, 0.5, 0), Vector3(-0.5, 0.5, 0), data)
	return mb.commit()
