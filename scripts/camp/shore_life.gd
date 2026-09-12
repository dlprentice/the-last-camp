class_name ShoreLife
extends Node3D

## Small-scale habitat: lily colonies in sheltered shallows and waterworn
## stones along the bank. Shared meshes keep the dressing inexpensive.

var field: TerrainField
var pad_positions: Array[Vector3] = []

func _init(p_field: TerrainField) -> void:
	field = p_field
	name = "ShoreLife"


func build() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 90426
	var pads: Array[Transform3D] = []
	var stones: Array[Transform3D] = []
	var flowers: Array[Transform3D] = []
	var stems: Array[Transform3D] = []
	var pad_radii: Array[float] = []
	for i in 1200:
		var angle := rng.randf() * TAU
		var shore := TerrainField.shore_point(angle)
		var radial := (shore - TerrainField.POND_CENTRE).normalized()
		var p := shore + radial * rng.randf_range(-1.3, 2.5)
		if p.distance_to(TerrainField.DOCK_START) < 3.0:
			continue
		var size := rng.randf_range(0.035, 0.16)
		var y := field.height_fast(p.x, p.y)
		var basis := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(size, size * 0.5, size * 0.8))
		stones.append(Transform3D(basis, Vector3(p.x, y + size * 0.08, p.y)))
	# Clusters live away from the travelled water and the canoe. Interleaved
	# rings of different sizes avoid uniform green discs along the whole bank.
	for colony in [Vector2(-28.0, 15.0), Vector2(-42.0, 11.0), Vector2(-36.0, -8.0)]:
		for i in 75:
			var angle := rng.randf() * TAU
			var radius := sqrt(rng.randf()) * 3.7
			var p: Vector2 = colony + Vector2(cos(angle), sin(angle)) * radius
			if not pad_allowed(field, p):
				continue
			var size := rng.randf_range(0.16, 0.36)
			var crowded := false
			for j in pad_positions.size():
				if p.distance_to(Vector2(pad_positions[j].x, pad_positions[j].z)) < (size + pad_radii[j]) * 0.85:
					crowded = true
					break
			if crowded:
				continue
			var origin := Vector3(p.x, TerrainField.WATER_LEVEL + 0.0015, p.y)
			var basis := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3.ONE * size)
			pads.append(Transform3D(basis, origin))
			stems.append(_stem_transform(origin))
			pad_positions.append(origin)
			pad_radii.append(size)
			if rng.randf() < 0.09:
				var offset := Vector3(cos(angle), 0.0, sin(angle)) * (size + 0.14)
				flowers.append(Transform3D(Basis(Vector3.UP, rng.randf() * TAU), origin + offset))
				stems.append(_stem_transform(origin + offset))
	var pad_material := ShaderMaterial.new()
	pad_material.shader = load("res://shaders/lily.gdshader")
	_instances("LilyPads", _pad_mesh(), pad_material, pads)
	var stone_material := ShaderMaterial.new()
	stone_material.shader = load("res://shaders/prop.gdshader")
	Camp.bind_prop_pbr(stone_material, "rock")
	Camp.bind_texture(stone_material, "noise_tex", "res://textures/noise_rgba.png")
	stone_material.set_shader_parameter("tile", 2.0)
	stone_material.set_shader_parameter("moss_amount", 0.08)
	stone_material.set_shader_parameter("tint", Color(0.7, 0.74, 0.71))
	_instances("WaterwornStones", PropMeshes.rock(9042), stone_material, stones)
	var petal_material := ShaderMaterial.new()
	petal_material.shader = load("res://shaders/lily.gdshader")
	petal_material.set_shader_parameter("blossom", true)
	_instances("WaterLilies", _flower_mesh(), petal_material, flowers)
	# Stalks reach from the bed to the exact moving centre of each leaf.
	var stalk := MeshBuilder.new()
	stalk.add_tube([Vector3(0.08, -1, -0.06), Vector3(-0.07, -0.65, 0.05), Vector3(0.045, -0.3, 0.015), Vector3.ZERO],
		[0.004, 0.0035, 0.0035, 0.004], 7, Color.WHITE, 1, 1, 0, true)
	var stalk_material := ShaderMaterial.new()
	stalk_material.shader = pad_material.shader
	stalk_material.set_shader_parameter("stalk", true)
	_instances("LilyStems", stalk.commit(), stalk_material, stems)


func _stem_transform(origin: Vector3) -> Transform3D:
	# Leaves spread out from irregular rhizome patches. Their petioles converge
	# on those roots instead of forming parallel vertical lines in the water.
	# The top stays attached to its pad and the root meets the actual bed.
	var phase := sin(origin.x * 12.3 + origin.z * 42.4)
	var bend := lerpf(0.65, 1.6, phase * 0.5 + 0.5)
	var rotation := Basis(Vector3.UP, phase * 1249.0)
	var patch := Vector2(floorf(origin.x / 1.4) + 0.5, floorf(origin.z / 1.4) + 0.5) * 1.4
	patch += Vector2(sin(patch.x * 4.7), cos(patch.y * 5.3)) * 0.18
	var root_offset := Vector3(patch.x - origin.x, 0, patch.y - origin.z)
	var curve_root := rotation * Vector3(0.08, 0, -0.06) * bend
	var height := origin.y - field.height(origin.x + root_offset.x, origin.z + root_offset.z) + 0.012
	var frame := rotation.scaled(Vector3(bend, height, bend))
	frame.y.x = curve_root.x - root_offset.x
	frame.y.z = curve_root.z - root_offset.z
	return Transform3D(frame, origin)


static func pad_allowed(terrain: TerrainField, p: Vector2) -> bool:
	var depth := terrain.water_depth(p.x, p.y)
	# The dock runs toward the pond centre; reserve that entire navigation lane.
	var lane: Array[Vector2] = [TerrainField.DOCK_START, TerrainField.POND_CENTRE]
	return depth > 0.20 and depth < 2.4 and TerrainField.distance_to_polyline(p, lane) > 3.0


static func _pad_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	# A thin cupped leaf with a narrow cleft. Radial subdivisions give the
	# reflected highlight a continuous curve instead of a triangle fan.
	const SECTORS := 128
	const RINGS := 8
	mb.add_vertex(Vector3.ZERO, Vector3.UP, Vector2.ONE * 0.5)
	for ring in range(1, RINGS + 1):
		var t := float(ring) / RINGS
		for i in range(SECTORS + 1):
			var a := lerpf(0.095, TAU - 0.095, float(i) / SECTORS)
			var radius := t * (0.94 + 0.022 * sin(a * 5.0) + 0.008 * sin(a * 13.0))
			# The rim turns up a couple of centimetres, unevenly, so the pad
			# catches light as a shallow dish rather than a flat disc.
			var y := pow(t, 3.0) * (0.016 + 0.007 * sin(a * 4.0 + 1.3) + 0.004 * sin(a * 9.0))
			var p := Vector3(cos(a) * radius, y, sin(a) * radius)
			mb.add_vertex(p, Vector3.UP, Vector2(p.x, p.z) * 0.5 + Vector2.ONE * 0.5)
	for i in SECTORS:
		mb.add_triangle(0, i + 1, i + 2)
	for ring in range(RINGS - 1):
		for i in SECTORS:
			var a := 1 + ring * (SECTORS + 1) + i
			mb.add_quad_facing(a, a + 1, a + SECTORS + 2, a + SECTORS + 1, Vector3.UP)
	mb.recompute_normals()
	return mb.commit()


static func _flower_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	# The peduncle continues below the water into the rhizome; the green
	# receptacle supports the cup at the surface instead of above a leaf.
	mb.add_tube([Vector3(0.02, -0.32, 0.01), Vector3(0.005, -0.06, 0), Vector3.ZERO], [0.004, 0.005, 0.007], 8, Color(0.065, 0.15, 0.035), 1, 1, 0, true)
	var calyx_start := mb.vertex_count()
	mb.add_displaced_sphere(6, 16, 0.023, func(_d: Vector3) -> float: return 1.0, Color(0.09, 0.19, 0.04))
	for i in range(calyx_start, mb.vertex_count()):
		mb.vertices[i].y = mb.vertices[i].y * 0.35 - 0.002
	# Temperate white water lily: lanceolate curved petals and a dense gold
	# centre. Both the cup and the petal edges have real geometry.
	for layer in 3:
		var count := 12 - layer * 2
		for petal in count:
			var a := float(petal) / count * TAU + float(layer) * 0.29
			var dir := Vector3(cos(a), 0, sin(a))
			var side := Vector3(-sin(a), 0, cos(a))
			var length := 0.115 - float(layer) * 0.021
			var start := mb.vertex_count()
			for row in 11:
				var t := float(row) / 10.0
				var width := pow(maxf(sin(t * PI), 0.0), 0.72) * (0.021 - layer * 0.003) + 0.0002
				var centre := dir * (0.013 + t * length) + Vector3.UP * (0.006 + layer * 0.008 + t * t * (0.024 + layer * 0.019))
				var cream := Color(0.88, 0.64, 0.18).lerp(Color(0.94, 0.93, 0.86), smoothstep(0.0, 0.33, t))
				for col in 7:
					var u := float(col) / 6.0 * 2.0 - 1.0
					var p := centre + side * width * u + Vector3.UP * u * u * sin(t * PI) * 0.006
					mb.add_vertex(p, Vector3.UP, Vector2(float(col) / 6.0, t), cream)
			for row in 10:
				for col in 6:
					var v := start + row * 7 + col
					mb.add_quad_facing(v, v + 1, v + 8, v + 7, Vector3.UP)
	for ring in 3:
		var count := 12 + ring * 6
		for i in count:
			var a := float(i) / count * TAU + ring * 0.4
			var dir := Vector3(cos(a), 0, sin(a))
			var radius := 0.005 + ring * 0.007
			var base := dir * radius + Vector3.UP * 0.016
			var tip := dir * (radius + 0.006) + Vector3.UP * (0.052 - ring * 0.005)
			mb.add_tube([base, base.lerp(tip, 0.75), tip], [0.0008, 0.0014, 0.001], 5, Color(0.95, 0.57 + ring * 0.07, 0.08), 1, 1, 0, true)
	mb.recompute_normals()
	return mb.commit()


func _instances(node_name: String, mesh: Mesh, mat: Material, transforms: Array[Transform3D]) -> void:
	if transforms.is_empty():
		return
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = mesh
	mm.instance_count = transforms.size()
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
	var mi := MultiMeshInstance3D.new()
	mi.name = node_name
	mi.multimesh = mm
	mi.material_override = mat
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	mi.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	add_child(mi)
