class_name CampKitchen
extends Node3D

## A modest, lived-in cooking corner beside the tent. Legs are measured down
## to the terrain; the tabletop is the single mounting datum for every prop.
const TOP := 0.82
const CLOTH_TOP := TOP + 0.004
var field: TerrainField
var feet: Array[Vector3] = []
var cloth: SoftBody3D
var cloth_pins := PackedInt32Array()
var _cloth_wind_points := PackedInt32Array()
var _cloth_wind_phases := PackedFloat32Array()
var _cloth_time := 0.0

func _init(p_field: TerrainField) -> void:
	field = p_field
	name = "CampKitchen"


func build() -> void:
	position = Vector3(TerrainField.TABLE.x, field.height(TerrainField.TABLE.x, TerrainField.TABLE.y), TerrainField.TABLE.y)
	rotation.y = -0.28
	var wood := PropMaterials.wood(Color(0.66, 0.57, 0.43), 0.48, 0.0, 0.93)
	var iron := FieldKit.solid(Color(0.11, 0.12, 0.12), 0.55, 0.7)
	for i in 6:
		var plank := FieldKit.rounded_box(Vector3(1.55, 0.042, 0.11), 0.007)
		# Offset the UVs per board so saw marks and wood grain never repeat.
		var arrays := plank.surface_get_arrays(0)
		var uv: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV]
		for j in uv.size():
			uv[j] += Vector2(float(i) * 0.271, float(i) * 0.613)
		arrays[Mesh.ARRAY_TEX_UV] = uv
		var mesh := ArrayMesh.new()
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
		FieldKit.add(self, mesh, wood, Vector3(0, TOP - 0.021, (float(i) - 2.5) * 0.115), "TableBoard")
	var frame := MeshBuilder.new()
	for x: float in [-0.60, 0.60]:
		for z: float in [-0.26, 0.26]:
			var local_foot := Vector3(x * 1.07, 0, z * 1.10)
			var world := to_global(local_foot)
			local_foot.y = field.height(world.x, world.z) - global_position.y - 0.025
			feet.append(to_global(local_foot))
			PropMeshes.add_timber(frame, [local_foot, Vector3(x, TOP - 0.035, z)], [0.034, 0.03], 5, 2, Color.WHITE, 1.2)
		# Flat bearers seat against the board undersides; the legs pass into
		# their ends. Round poles touching at a tangent left visible daylight.
		FieldKit.add(self, FieldKit.rounded_box(Vector3(0.083, 0.090, 0.66), 0.004), wood, Vector3(x, TOP - 0.078, 0), "CrossBearer")
	for z: float in [-0.26, 0.26]:
		FieldKit.add(self, FieldKit.rounded_box(Vector3(1.26, 0.105, 0.047), 0.004), wood, Vector3(0, TOP - 0.090, z), "TableApron")
	PropMeshes.add_timber(frame, [Vector3(-0.6, 0.2, -0.26), Vector3(0.6, 0.66, -0.26)], [0.02, 0.02], 4, 1, Color.WHITE, 1.2)
	PropMeshes.add_timber(frame, [Vector3(-0.6, 0.66, 0.26), Vector3(0.6, 0.2, 0.26)], [0.02, 0.02], 4, 1, Color.WHITE, 1.2)
	FieldKit.add(self, frame.commit(), wood, Vector3.ZERO, "TrestleFrame")
	var nails := MeshBuilder.new()
	for x: float in [-0.60, 0.60]:
		for i in 6:
			var z := (float(i) - 2.5) * 0.115
			nails.add_tube([Vector3(x, TOP - 0.002, z), Vector3(x, TOP + 0.001, z)], [0.005, 0.005], 8, Color.WHITE, 1, 1, 0, true)
	FieldKit.add(self, nails.commit(), iron, Vector3.ZERO, "NailHeads")
	_box(Vector3(0, 0.41, 0), Vector3(1.55, 0.82, 0.69))
	# The tall player blocker has rounded collision corners which sit inside
	# the visible boards. A thin, cloth-only shape follows the actual top.
	var cloth_support := StaticBody3D.new()
	cloth_support.name = "ClothSupport"
	cloth_support.collision_layer = 1 << 7
	cloth_support.collision_mask = 0
	var support_shape := CollisionShape3D.new()
	var top_box := BoxShape3D.new()
	top_box.size = Vector3(1.554, 0.042, 0.689)
	top_box.margin = 0.001
	support_shape.shape = top_box
	support_shape.position.y = TOP - 0.021
	cloth_support.add_child(support_shape)
	add_child(cloth_support)
	_add_tablecloth()
	FieldKit.kettle(self, Vector3(-0.36, CLOTH_TOP, -0.02))
	var mug := FieldKit.mug(self, Vector3(0.12, CLOTH_TOP, -0.15), Color(0.73, 0.72, 0.57))
	mug.rotation.y = -0.5
	FieldKit.mug(self, Vector3(0.34, CLOTH_TOP, 0.12), Color(0.20, 0.33, 0.34)).rotation.y = 1.6
	_add_journal()
	_add_crate(wood)


func _add_tablecloth() -> void:
	var mb := MeshBuilder.new()
	const NX := 76
	const NZ := 48
	for j in range(NZ + 1):
		for i in range(NX + 1):
			var u := (float(i) / NX - 0.5) * 1.90
			var v := (float(j) / NZ - 0.5) * 1.20
			# Both boundaries land exactly on grid vertices. The 7.5 mm side
			# allowance clears the board ends while the skirt rounds the edge.
			var excess := Vector2(maxf(absf(u) - 0.775, 0), maxf(absf(v) - 0.350, 0))
			var drop := excess.length()
			var bend := minf(drop / 0.014, PI * 0.5)
			var tail := maxf(drop - 0.014 * PI * 0.5, 0.0)
			var free_weight := smoothstep(0.015, 0.15, drop)
			var folds := (sin(u * 19.0 + v * 11.0) * 0.005 + sin(u * 8.0 - v * 17.0 + 0.6) * 0.003) * free_weight
			var spread := 0.014 * sin(bend) + tail * 0.20 + folds
			var down := 0.014 * (1.0 - cos(bend)) + tail * 0.98
			down += (sin(u * 7.0 + v * 5.0) * 0.003 + sin(u * 17.0 - v * 9.0) * 0.0015) * free_weight
			var outward := excess.normalized()
			# Angular separation preserves a fan of fabric around each corner,
			# rather than collapsing its two-dimensional mesh onto a line.
			var p := Vector3(clampf(u, -0.775, 0.775) + signf(u) * outward.x * spread,
				CLOTH_TOP - down, clampf(v, -0.350, 0.350) + signf(v) * outward.y * spread)
			p.y += _linen_crease(clampf(u, -0.775, 0.775), clampf(v, -0.350, 0.350)) * (1.0 - smoothstep(0.0, 0.06, drop))
			var normal := Vector3(signf(u) * excess.x * 60.0, 1.0, signf(v) * excess.y * 60.0).normalized()
			var index := mb.add_vertex(p, normal, Vector2(u + 0.95, v + 0.60), Color.WHITE)
			# Secure the narrow wrap until it clears the board underside. Point
			# collisions alone let a free vertex slip below the thin board and
			# pull its connecting triangle straight through the visible end grain.
			if drop <= 0.0751:
				cloth_pins.append(index)
			elif i % 4 == 0 and j % 4 == 0:
				_cloth_wind_points.append(index)
				_cloth_wind_phases.append(u * 8.0 + v * 5.0)
	for j in NZ:
		for i in NX:
			var a := j * (NX + 1) + i
			# A connected sheet needs consistent winding even where it folds
			# down vertically. Per-face UP tests flipped alternate skirt cells.
			mb.add_quad_indices(a, a + NX + 1, a + NX + 2, a + 1)
	mb.recompute_normals()
	mb.recompute_tangents()
	cloth = SoftBody3D.new()
	cloth.name = "LinenTablecloth"
	cloth.mesh = mb.commit()
	var material := ShaderMaterial.new()
	material.shader = load("res://shaders/tablecloth.gdshader")
	Camp.bind_texture(material, "weave_tex", "res://textures/canvas_albedo.png")
	Camp.bind_texture(material, "weave_normal", "res://textures/canvas_normal.png")
	cloth.material_override = material
	cloth.total_mass = 0.22
	cloth.linear_stiffness = 0.88
	cloth.damping_coefficient = 0.12
	cloth.simulation_precision = 6
	cloth.collision_layer = 0
	cloth.collision_mask = 1 << 7
	cloth.ray_pickable = false
	add_child(cloth)
	for point in cloth_pins:
		cloth.set_point_pinned(point, true)


static func _linen_crease(u: float, v: float) -> float:
	var p := Vector2(u, v)
	# Keep the fabric flat under the objects, easing into shallow creases in
	# the open cloth. These are folds of clean linen, not painted dirt.
	var clear := smoothstep(0.17, 0.20, p.distance_to(Vector2(-0.36, -0.02)))
	clear *= smoothstep(0.055, 0.085, p.distance_to(Vector2(0.12, -0.15)))
	clear *= smoothstep(0.055, 0.085, p.distance_to(Vector2(0.34, 0.12)))
	var book := (p - Vector2(0.42, -0.12)).abs() - Vector2(0.135, 0.165)
	clear *= smoothstep(0.0, 0.03, maxf(book.x, book.y))
	var fold := u * 0.9 + v * 0.5 + 0.19
	return clear * (0.0014 * exp(-fold * fold / 0.0003) + 0.0008 * pow(maxf(cos(u * 7.0 + v * 5.0), 0.0), 8.0))


func _physics_process(delta: float) -> void:
	_cloth_time += delta
	if cloth == null or Game.world == null:
		return
	var wind := WorldController.WIND_DIRECTION.normalized()
	var gust: float = Game.world.wind_strength() * (0.75 + 0.25 * sin(_cloth_time * 1.7))
	cloth.apply_central_force(Vector3(wind.x, 0.0, wind.y) * gust * 0.14)
	# Each sample represents a 10 cm square of cloth. Spatial gust phases
	# disturb the free hem gently instead of tilting the whole skirt together.
	const SAMPLE_AREA := 0.10 * 0.10
	for i in _cloth_wind_points.size():
		var pulse := sin(_cloth_time * 2.4 - _cloth_wind_phases[i])
		var pressure := gust * (0.22 + 0.13 * pulse)
		cloth.apply_force(_cloth_wind_points[i], Vector3(wind.x * pressure, pulse * gust * 0.045, wind.y * pressure) * SAMPLE_AREA)


func _add_journal() -> void:
	var root := Node3D.new()
	root.name = "FieldJournal"
	root.position = Vector3(0.42, CLOTH_TOP, -0.12)
	root.rotation.y = 0.14
	add_child(root)
	var leather := FieldKit.solid(Color(0.22, 0.14, 0.075), 0.85)
	FieldKit.add(root, FieldKit.rounded_box(Vector3(0.21, 0.003, 0.27), 0.001), leather, Vector3(0, 0.0025, 0))
	FieldKit.add(root, FieldKit.rounded_box(Vector3(0.196, 0.023, 0.255), 0.002), FieldKit.solid(Color(0.66, 0.62, 0.48), 0.98), Vector3(0.005, 0.016, 0))
	FieldKit.add(root, FieldKit.rounded_box(Vector3(0.21, 0.003, 0.27), 0.001), leather, Vector3(0, 0.03, 0))
	var label := Label3D.new()
	label.text = "FIELD\nNOTES"
	label.font_size = 48
	label.pixel_size = 0.0006
	label.modulate = Color(0.66, 0.55, 0.33)
	label.outline_size = 0
	label.no_depth_test = false
	label.position = Vector3(0, 0.032, -0.026)
	label.rotation.x = -PI * 0.5
	root.add_child(label)
	# Pencil lies wholly on the book, aligned to the long edge.
	var mb := MeshBuilder.new()
	mb.add_tube([Vector3(0.069, 0.035, -0.09), Vector3(0.069, 0.035, 0.07), Vector3(0.069, 0.035, 0.086)], [0.003, 0.003, 0.0002], 6)
	FieldKit.add(root, mb.commit(), PropMaterials.wood(Color(0.66, 0.48, 0.21), 0.1, 0))


func _add_crate(wood: Material) -> void:
	var root := Node3D.new()
	root.name = "SlattedSupplyCrate"
	root.position = Vector3(-0.13, 0, 0.83)
	root.rotation.y = 0.12
	var world := to_global(root.position)
	root.position.y = field.height(world.x, world.z) - global_position.y + 0.015
	add_child(root)
	for row in 4:
		for side: float in [-1.0, 1.0]:
			FieldKit.add(root, FieldKit.rounded_box(Vector3(0.64, 0.073, 0.019), 0.004), wood, Vector3(0, 0.055 + float(row) * 0.079, side * 0.23))
			FieldKit.add(root, FieldKit.rounded_box(Vector3(0.019, 0.073, 0.44), 0.004), wood, Vector3(side * 0.31, 0.055 + float(row) * 0.079, 0))
	for side_x: float in [-1.0, 1.0]:
		for side_z: float in [-1.0, 1.0]:
			FieldKit.add(root, FieldKit.rounded_box(Vector3(0.032, 0.36, 0.032), 0.003), wood, Vector3(side_x * 0.285, 0.18, side_z * 0.201))
	for i in 4:
		FieldKit.add(root, FieldKit.rounded_box(Vector3(0.61, 0.022, 0.107), 0.004), wood, Vector3(0, 0.343, (float(i) - 1.5) * 0.112))
	FieldKit.blanket(root, Vector3(0, 0.45, 0))
	_box(root.position + Vector3(0, 0.18, 0), Vector3(0.66, 0.36, 0.48))


func _box(at: Vector3, size: Vector3) -> void:
	var body := StaticBody3D.new()
	body.collision_layer = 1
	body.collision_mask = 0
	body.set_meta("surface", &"wood")
	body.position = at
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = size
	shape.shape = box
	body.add_child(shape)
	add_child(body)
