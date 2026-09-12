class_name ForestFloorDressing
extends Node3D

## Coarse forest-floor structure that makes the woodland read as an ecosystem,
## not trees planted into grass. Placement is deterministic and habitat-driven.
## Fallen logs receive simple collision. Low decorative roots are conformed to
## the terrain and kept off the walked paths; they are not tall obstacles.

const FALLEN_LOG_TARGET := 58
const FALLEN_LOG_ATTEMPTS := 1800
const MOSS_STONE_TARGET := 260
const MOSS_STONE_ATTEMPTS := 3600
const ROOT_MAX_DISTANCE := 76.0

var _camp: Camp
var _field: TerrainField
var _plan: ScenePlan
var _built := false


func _ready() -> void:
	name = "ForestFloorDressing"
	set_process(false)


func setup(camp: Camp) -> void:
	if _built:
		return
	_camp = camp
	_field = camp.field
	_plan = camp.plan
	_build()
	_built = true


func _build() -> void:
	_build_buttress_roots()
	_build_fallen_logs()
	_build_moss_stones()


# --------------------------------------------------------------- tree grounding

func _build_buttress_roots() -> void:
	var groups := {}
	var material := PropMaterials.bark("bark_oak", 0.62)
	var count := 0
	for tree in _plan.near_trees():
		if tree.position.length() > ROOT_MAX_DISTANCE or tree.kind not in [TreeSpecies.Kind.OAK, TreeSpecies.Kind.ALDER]:
			continue
		if tree.position.distance_to(TerrainField.FIRE) < 11.0:
			continue
		var rng := RandomNumberGenerator.new()
		rng.seed = tree.seed_value ^ 0x72A4E1
		var roots := 5 if tree.kind == TreeSpecies.Kind.OAK else 3
		var phase := rng.randf() * TAU
		for i in roots:
			var angle := phase + i * TAU / float(roots) + rng.randf_range(-0.30, 0.30)
			var direction := Vector2(cos(angle), sin(angle))
			var length := rng.randf_range(0.80, 1.75) * tree.scale
			var radius := rng.randf_range(0.12, 0.22) * tree.scale
			var curve := root_curve(_field, tree.position, direction, length, radius, rng.randf_range(-0.18, 0.18))
			var clear := true
			for point: Vector3 in curve.points:
				if _field.walking_distance(Vector2(point.x, point.z)) < radius + 0.35:
					clear = false
					break
			if not clear:
				continue
			var key := Vector2i(floori(tree.position.x / 24.0), floori(tree.position.y / 24.0))
			if not groups.has(key):
				groups[key] = MeshBuilder.new()
			var builder: MeshBuilder = groups[key]
			builder.add_tube(curve.points, curve.radii, 10, Color.WHITE, 1.0, 1.0 / 1.2, rng.randf(), true)
			count += 1
	for key in groups:
		var node := MeshInstance3D.new()
		node.name = "RootFlare_%d_%d" % [key.x, key.y]
		node.mesh = groups[key].commit(null, true)
		node.material_override = material
		node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		node.gi_mode = GeometryInstance3D.GI_MODE_STATIC
		add_child(node)
	print("Forest floor: %d terrain-conforming tapered roots" % count)


static func root_curve(field: TerrainField, origin: Vector2, direction: Vector2,
		length: float, radius: float, bend: float) -> Dictionary:
	var points: Array[Vector3] = []
	var radii: Array[float] = []
	var side := Vector2(-direction.y, direction.x)
	for i in 11:
		var t := i / 10.0
		var at := origin + direction * (0.07 + length * t) + side * sin(t * PI) * bend
		var r := radius * pow(1.0 - t, 1.8) + 0.003
		var bury := r * 0.55 + smoothstep(0.8, 1.0, t) * 0.012
		points.append(Vector3(at.x, field.surface_height(at.x, at.y) - bury, at.y))
		radii.append(r)
	return {points = points, radii = radii}


## Apply the same fracture field to bark and cap boundary positions. The
## end-grain rim becomes irregular without separating the two surfaces.
static func fractured_log(radius: float, seed_value: int) -> ArrayMesh:
	var source := PropMeshes.bark_log_mesh(1.0, radius, seed_value, 0.09)
	var result := ArrayMesh.new()
	for surface in source.get_surface_count():
		var arrays := source.surface_get_arrays(surface)
		var builder := MeshBuilder.new()
		builder.vertices = arrays[Mesh.ARRAY_VERTEX]
		builder.normals = arrays[Mesh.ARRAY_NORMAL]
		builder.uvs = arrays[Mesh.ARRAY_TEX_UV]
		builder.colors = arrays[Mesh.ARRAY_COLOR]
		builder.indices = arrays[Mesh.ARRAY_INDEX]
		for i in builder.vertices.size():
			var point := builder.vertices[i]
			var end := smoothstep(0.34, 0.49, absf(point.x))
			var angle := atan2(point.z, point.y)
			var grain := sin(angle * 3.0 + seed_value * 0.13) * 0.6 + cos(angle * 7.0 - seed_value * 0.07) * 0.4
			point.x += signf(point.x) * end * grain * radius * 0.23
			builder.vertices[i] = point
		builder.recompute_normals()
		builder.recompute_tangents()
		result = builder.commit(null, true, result)
	return result


# ------------------------------------------------------------------- dead wood

func _build_fallen_logs() -> void:
	var bark := PropMaterials.bark("bark_oak", 0.72)
	var end_grain := PropMaterials.wood(Color(0.58, 0.48, 0.34), 0.88, 0.18, 1.15)
	var meshes: Array[ArrayMesh] = []
	for i in 4:
		var mesh := fractured_log(0.11 + float(i) * 0.012, 9600 + i * 37)
		mesh.surface_set_material(0, bark)
		if mesh.get_surface_count() > 1:
			mesh.surface_set_material(1, end_grain)
		meshes.append(mesh)
	var groups: Array[Array] = [[], [], [], []]
	var collisions: Array[Dictionary] = []
	var rng := RandomNumberGenerator.new()
	rng.seed = 97231
	var placed := 0
	for attempt in FALLEN_LOG_ATTEMPTS:
		if placed >= FALLEN_LOG_TARGET:
			break
		var angle := rng.randf() * TAU
		var radius := sqrt(lerpf(30.0 * 30.0, 118.0 * 118.0, rng.randf()))
		var p := Vector2(cos(angle), sin(angle)) * radius
		if not _forest_floor_allowed(p, 4.2):
			continue
		var cover := _field.woodland_cover(p.x, p.y)
		var shore := _shore_offset(p)
		var wet_bonus := 1.0 - smoothstep(6.0, 26.0, shore)
		if rng.randf() > clampf(cover * 0.72 + wet_bonus * 0.20, 0.0, 0.92):
			continue
		var yaw := rng.randf() * TAU
		var length := rng.randf_range(1.5, 3.7)
		var radius_scale := rng.randf_range(0.85, 1.40)
		var direction2 := Vector2(cos(yaw), sin(yaw))
		var a := p - direction2 * length * 0.42
		var b := p + direction2 * length * 0.42
		if not _forest_floor_allowed(a, 3.5) or not _forest_floor_allowed(b, 3.5):
			continue
		var ya := _field.height_fast(a.x, a.y)
		var yb := _field.height_fast(b.x, b.y)
		var x_axis := Vector3(direction2.x, (yb - ya) / maxf(length * 0.84, 0.1), direction2.y).normalized()
		var up_hint := _field.normal(p.x, p.y, 0.45)
		var z_axis := x_axis.cross(up_hint).normalized()
		var y_axis := z_axis.cross(x_axis).normalized()
		var basis := Basis(x_axis, y_axis, z_axis) * Basis.from_scale(Vector3(length, radius_scale, radius_scale))
		var variant := rng.randi() % meshes.size()
		# Support the whole rotated timber, not just its middle. A small
		# intentional burial gives deadwood a settled contact with the soil.
		var y := -INF
		for surface in meshes[variant].get_surface_count():
			for vertex: Vector3 in meshes[variant].surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]:
				var local := basis * vertex
				y = maxf(y, _field.surface_height(p.x + local.x, p.y + local.z) - local.y)
		y -= 0.035
		var transform := Transform3D(basis, Vector3(p.x, y, p.y))
		groups[variant].append(transform)
		collisions.append({"position": Vector3(p.x, y, p.y), "basis": Basis(x_axis, y_axis, z_axis),
			"length": length * 0.84, "radius": (0.11 + float(variant) * 0.012) * radius_scale})
		placed += 1
	for i in meshes.size():
		_add_multimesh("FallenLogs_%d" % i, meshes[i], null, groups[i], true)
	for info in collisions:
		_add_log_collision(info)
	print("Forest floor: %d fallen logs" % placed)


func _add_log_collision(info: Dictionary) -> void:
	var body := StaticBody3D.new()
	body.name = "DeadwoodCollision"
	body.collision_layer = 1
	body.collision_mask = 0
	body.set_meta("surface", &"wood")
	var shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = maxf(float(info.radius) * 0.78, 0.07)
	capsule.height = maxf(float(info.length), capsule.radius * 2.1)
	shape.shape = capsule
	# CapsuleShape3D runs along local Y; rotate local Y onto the log's local X.
	var align := Basis(Vector3.FORWARD, PI * 0.5)
	body.transform = Transform3D(info.basis * align, info.position)
	body.add_child(shape)
	add_child(body)


# ---------------------------------------------------------------- mossy stones

func _build_moss_stones() -> void:
	var materials: Array[ShaderMaterial] = [
		PropMaterials.triplanar("rock", Color(0.56, 0.60, 0.54), 0.82, 0.58),
		PropMaterials.triplanar("rock", Color(0.48, 0.52, 0.47), 0.74, 0.76),
	]
	var meshes: Array[ArrayMesh] = []
	for i in 4:
		meshes.append(PropMeshes.rock(9800 + i * 23, 1.0))
	var groups: Array[Array] = [[], [], [], [], [], [], [], []]
	var rng := RandomNumberGenerator.new()
	rng.seed = 98117
	var placed := 0
	for attempt in MOSS_STONE_ATTEMPTS:
		if placed >= MOSS_STONE_TARGET:
			break
		var angle := rng.randf() * TAU
		var radius := sqrt(lerpf(15.0 * 15.0, 115.0 * 115.0, rng.randf()))
		var p := Vector2(cos(angle), sin(angle)) * radius
		if not _forest_floor_allowed(p, 1.6):
			continue
		var cover := _field.woodland_cover(p.x, p.y)
		var shore := _shore_offset(p)
		var moisture := maxf(cover, 1.0 - smoothstep(4.0, 30.0, shore))
		if rng.randf() > 0.20 + moisture * 0.52:
			continue
		var s := rng.randf_range(0.08, 0.34) * lerpf(0.85, 1.28, moisture)
		var normal := _field.normal(p.x, p.y, 0.28)
		var tangent := Vector3(cos(angle + rng.randf_range(-1.6, 1.6)), 0.0, sin(angle + rng.randf_range(-1.6, 1.6)))
		tangent = (tangent - normal * tangent.dot(normal)).normalized()
		var bitangent := tangent.cross(normal).normalized()
		var basis := Basis(tangent, normal, bitangent)
		basis = basis.rotated(normal, rng.randf() * TAU) * Basis.from_scale(Vector3(s * rng.randf_range(0.85, 1.28), s * rng.randf_range(0.55, 0.92), s))
		var y := _field.height_fast(p.x, p.y)
		var transform := Transform3D(basis, Vector3(p.x, y - s * rng.randf_range(0.22, 0.42), p.y))
		var mesh_index := rng.randi() % meshes.size()
		var mat_index := 1 if moisture > 0.62 else 0
		groups[mesh_index * 2 + mat_index].append(transform)
		placed += 1
	for mesh_index in meshes.size():
		for mat_index in materials.size():
			var group_index := mesh_index * 2 + mat_index
			_add_multimesh("MossStones_%d_%d" % [mesh_index, mat_index], meshes[mesh_index], materials[mat_index], groups[group_index], true)
	print("Forest floor: %d moss stones" % placed)


# ------------------------------------------------------------------- helpers

func _forest_floor_allowed(p: Vector2, trail_clearance: float) -> bool:
	if p.distance_to(TerrainField.FIRE) < 11.0 or p.distance_to(TerrainField.TENT) < 8.0:
		return false
	if _field.trail_distance(p.x, p.y) < trail_clearance:
		return false
	if TerrainField.camp_wear(p) > 0.10:
		return false
	var y := _field.height_fast(p.x, p.y)
	if y < TerrainField.WATER_LEVEL + 0.12:
		return false
	if _field.slope(p.x, p.y) > 0.62:
		return false
	return true


static func _shore_offset(pos: Vector2) -> float:
	var delta := pos - TerrainField.POND_CENTRE
	return delta.length() - TerrainField.pond_radius_at(atan2(delta.y, delta.x))


func _add_multimesh(node_name: String, mesh: ArrayMesh, material: Material,
		transforms: Array, cast_shadow: bool) -> MultiMeshInstance3D:
	if transforms.is_empty():
		return null
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = mesh
	mm.instance_count = transforms.size()
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
	var local_bounds := mesh.get_aabb().grow(0.18)
	var bounds: AABB = transforms[0] * local_bounds
	for i in range(1, transforms.size()):
		bounds = bounds.merge(transforms[i] * local_bounds)
	mm.custom_aabb = bounds
	var node := MultiMeshInstance3D.new()
	node.name = node_name
	node.multimesh = mm
	if material != null:
		node.material_override = material
	node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON if cast_shadow else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	node.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	add_child(node)
	return node
