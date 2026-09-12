extends "res://tests/test_case.gd"


func test_blade_normals_follow_the_rendered_curve() -> void:
	# Recover tangents from the actual mesh positions instead of duplicating
	# the blade formula. The coarsest mesh still has three centre-line samples.
	# Lean along the ribbon width can barely rotate its normal, so validity
	# depends on perpendicularity, not a minimum root-to-tip angular change.
	for profile: Array in [[7, 1, 0.018, 77, 1.0, 1.0, 0.0], [7, 5, 0.018, 77, 1.0, 1.0, 0.0],
			[24, 5, 0.011, 4021, 3.0, 2.15, 0.18], [18, 5, 0.020, 4059, 3.2, 2.5, 0.26]]:
		var blades: int = profile[0]
		var segments: int = profile[1]
		var mesh: ArrayMesh = GrassPlanter.clump_mesh.callv(profile)
		var arrays := mesh.surface_get_arrays(0)
		var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
		var uv2s: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV2]
		var stride := (segments + 1) * 2 + 1
		for blade in blades:
			var base := blade * stride
			var centres := PackedVector3Array()
			for row in segments + 1:
				centres.append((vertices[base + row * 2] + vertices[base + row * 2 + 1]) * 0.5)
			centres.append(vertices[base + stride - 1])
			var across := (vertices[base + 1] - vertices[base]).normalized()
			for row in centres.size():
				var along: Vector3
				if row == 0:
					along = -3.0 * centres[0] + 4.0 * centres[1] - centres[2]
				elif row == centres.size() - 1:
					along = 3.0 * centres[row] - 4.0 * centres[row - 1] + centres[row - 2]
				else:
					along = centres[row + 1] - centres[row - 1]
				var index := base + mini(row * 2, stride - 1)
				var dy_dv := along.y * float(segments + 1) * 0.5
				assert_near(uv2s[index].y, dy_dv, 0.0001, "wind derivative agrees with the rendered arch")
				assert_gt(dy_dv, 0.0, "arched blades do not fold through themselves vertically")
				along = along.normalized()
				var normal := normals[index]
				assert_true(normal.is_finite(), "each blade normal is finite")
				assert_near(normal.length(), 1.0, 0.0001, "each blade normal has unit length")
				# Recovering a width vector from millimetre-wide float32 positions
				# loses a little more precision on the longer arched blades.
				assert_near(normal.dot(across), 0.0, 0.0002, "normal is perpendicular to blade width")
				assert_near(normal.dot(along), 0.0, 0.0001, "normal follows the curved centre line")


func test_seed_geometry_has_finite_surfaces_and_grounded_stems() -> void:
	for kind in 2:
		var mesh := MeadowPlants.seed_heads(kind, 4021 + kind * 38)
		var arrays := mesh.surface_get_arrays(0)
		var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
		var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
		var invalid := 0
		var degenerate := 0
		var rooted := 0
		for i in vertices.size():
			if not vertices[i].is_finite() or not normals[i].is_finite() or absf(normals[i].length() - 1.0) > 0.002:
				invalid += 1
			if absf(vertices[i].y) < 0.002:
				rooted += 1
		for i in range(0, indices.size(), 3):
			var a := vertices[indices[i]]
			var b := vertices[indices[i + 1]]
			var c := vertices[indices[i + 2]]
			if (b - a).cross(c - a).length_squared() < 1e-18:
				degenerate += 1
		assert_eq(invalid, 0, "seed stems and spikelets have usable geometry normals")
		assert_eq(degenerate, 0, "tiny seed surfaces retain area after mesh packing")
		assert_gt(rooted, 8, "seed stems meet the soil instead of floating above basal leaves")


func test_tussock_footprints_preserve_routes_props_and_pond() -> void:
	var field := TerrainField.new()
	field.bake_height_grid(48.0, 0.5)
	var understory := Understory.new(field, null)
	var groups := understory._plan_meadow_grasses(1800, 44.0)
	var meshes: Array[ArrayMesh] = [GrassPlanter.clump_mesh(24, 5, 0.011, 4021, 3.0, 2.15, 0.18),
		GrassPlanter.clump_mesh(18, 5, 0.020, 4059, 3.2, 2.5, 0.26)]
	var forms := PackedInt32Array([0, 0])
	var min_route := INF
	var max_wear := 0.0
	var min_water_clearance := INF
	var min_height := INF
	var max_height := 0.0
	var seeds := 0
	for key: Vector3i in groups:
		var group: Dictionary = groups[key]
		var vertices: PackedVector3Array = meshes[key.z].surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
		forms[key.z] += group.transforms.size()
		seeds += group.seed_transforms.size()
		for transform: Transform3D in group.transforms:
			min_height = minf(min_height, transform.basis.y.length())
			max_height = maxf(max_height, transform.basis.y.length())
			assert_near(transform.origin.y, field.height_fast(transform.origin.x, transform.origin.z) - 0.018,
				0.0001, "tussock roots use the same soil surface as the base sward")
			# Check the emitted curved mesh, not only the centre accepted by the
			# scatterer: tall leaning leaves can otherwise pierce the clear path.
			for vertex in vertices:
				var at := transform * vertex
				var p := Vector2(at.x, at.z)
				min_route = minf(min_route, field.walking_distance(p))
				max_wear = maxf(max_wear, TerrainField.camp_wear(p))
				min_water_clearance = minf(min_water_clearance, field.height_fast(p.x, p.y) - TerrainField.WATER_LEVEL)
	assert_gt(forms[0], 0, "open panicle habitat is populated")
	assert_gt(forms[1], 0, "compact seed-head habitat is populated")
	assert_gt(seeds, 0, "some basal tufts carry seed stems")
	assert_gt(max_height - min_height, 0.20, "tussocks retain a visible range of heights")
	assert_gt(min_route, 0.52, "full tuft meshes clear the walking route")
	assert_lt(max_wear, 0.081, "full tuft meshes clear fire, seats, tent, table and woodpile pads")
	assert_gt(min_water_clearance, 0.09, "terrestrial tuft footprints stay above the pond")
	understory.free()
