extends TestCase

## The photoscanned dressing is deterministic, sits on the ground, keeps off
## the walked routes and the water, and its large pieces stay clear of every
## authored camera path.


func _build() -> ScannedDressing:
	var field := TerrainFieldEnhanced.new()
	var plan := ScenePlan.new(field)
	plan.build()
	var dressing := ScannedDressing.new()
	dressing.setup_from(field, plan)
	return dressing


## Placements are read from the dressing's own records: the headless dummy
## renderer does not store MultiMesh instance data, so reading transforms
## back from the batches would test nothing.
func _placements(dressing: ScannedDressing) -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	for key: String in dressing._placements:
		var model: String = key.split("|")[0]
		for xform: Transform3D in dressing._placements[key].transforms:
			out.append({model = model, key = key, origin = xform.origin})
	return out


func _origins(dressing: ScannedDressing) -> Array[String]:
	var out: Array[String] = []
	for item in _placements(dressing):
		var o: Vector3 = item.origin
		out.append("%s %.3f %.3f %.3f" % [item.key, o.x, o.y, o.z])
	out.sort()
	return out


func test_placement_is_deterministic_and_populated() -> void:
	var a := _build()
	var b := _build()
	assert_eq(_origins(a), _origins(b), "two builds place every scanned instance identically")
	assert_gt(a.counts.get("fern_02", 0), 400, "scanned ferns fill the shaded woodland")
	assert_gt(a.counts.get("rock_moss_set_01", 0) + a.counts.get("rock_moss_set_02", 0), 20, "mossy rock sets are present")
	assert_gt(a.counts.get("tree_stump_01", 0) + a.counts.get("tree_stump_02", 0), 8, "stumps are present")
	# Scanned root balls read as mounds of dirt in the camp shots and are no longer placed.
	assert_eq(a.counts.get("root_cluster_02", 0) + a.counts.get("single_root", 0) + a.counts.get("root_cluster_01", 0), 0, "no scanned root balls")
	assert_gt(a.batches.size(), 100, "instances are batched per cell")
	a.free()
	b.free()


func test_instances_are_grounded_off_routes_and_finite() -> void:
	var dressing := _build()
	var field := dressing._field
	var checked := 0
	var far_from_ground := 0
	for item in _placements(dressing):
		var o: Vector3 = item.origin
		var model: String = item.model
		assert_true(is_finite(o.x) and is_finite(o.y) and is_finite(o.z), "finite origin in %s" % model)
		var ground := field.height(o.x, o.z)
		if absf(o.y - ground) > 1.2:
			far_from_ground += 1
		assert_gt(ground, TerrainField.WATER_LEVEL + 0.04, "%s stays out of the pond at %s" % [model, o])
		assert_gt(field.walking_distance(Vector2(o.x, o.z)), 0.6, "%s keeps off the walked routes at %s" % [model, o])
		checked += 1
	assert_eq(far_from_ground, 0, "every scanned instance sits within reach of the ground")
	assert_gt(checked, 2000, "the whole dressing was checked")
	dressing.free()


func test_large_pieces_clear_every_camera_path() -> void:
	var dressing := _build()
	var samples := ScannedDressing.camera_samples(dressing._field)
	var large := ["tree_stump", "dead_tree_trunk", "boulder_01", "rock_moss_set"]
	var checked := 0
	for item in _placements(dressing):
		var model: String = item.model
		var is_large := false
		for prefix in large:
			if model.begins_with(prefix):
				is_large = true
		if not is_large:
			continue
		var o: Vector3 = item.origin
		var ground := dressing._field.height(o.x, o.z)
		var nearest := INF
		for s in samples:
			if s.y > ground + 3.0:
				continue
			nearest = minf(nearest, Vector2(s.x, s.z).distance_to(Vector2(o.x, o.z)))
		assert_gt(nearest, 2.0, "%s at %s is %.2f m from a camera path" % [model, o, nearest])
		checked += 1
	assert_gt(checked, 30, "large scanned pieces were checked against the camera paths")
	dressing.free()


func test_camp_props_are_placed_whole_and_grounded() -> void:
	var dressing := _build()
	var samples := ScannedDressing.camera_samples(dressing._field)
	var expected := ["hatchet", "wooden_bucket_01", "wooden_crate_01", "wicker_basket_01", "pot_enamel_01",
		"brass_pot_01", "handsaw_wood", "modified_thermos", "wooden_lantern_01"]
	for model in expected:
		assert_eq(dressing.counts.get(model, 0), 1, "%s is placed exactly once" % model)
	assert_eq(dressing.props.size(), expected.size(), "every camp prop is a whole scene instance")
	for prop in dressing.props:
		var o := prop.transform.origin
		var ground := dressing._field.height(o.x, o.z)
		assert_lt(absf(o.y - ground), 0.9, "%s rests near the ground (%.2f vs %.2f)" % [prop.name, o.y, ground])
		var nearest := INF
		for s in samples:
			if s.y > ground + 2.5:
				continue
			nearest = minf(nearest, Vector2(s.x, s.z).distance_to(Vector2(o.x, o.z)))
		assert_gt(nearest, 0.6, "%s is %.2f m from a camera path" % [prop.name, nearest])
	dressing.free()
