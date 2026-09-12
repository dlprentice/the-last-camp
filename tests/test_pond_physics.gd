extends TestCase


func test_buoyancy_supports_weight_and_drag_opposes_motion() -> void:
	var mass := Canoe.FLOATING_MASS
	var points := Canoe.FLOAT_POINTS.size()
	var resting := PondSurface.buoyancy(Canoe.DRAFT, 0.0, mass, Canoe.DRAFT, points)
	assert_near(resting * points, mass * 9.8, 0.0001, "rest draft supports total weight")
	assert_gt(PondSurface.buoyancy(Canoe.DRAFT, -0.1, mass, Canoe.DRAFT, points), resting, "descending hull is resisted")
	assert_lt(PondSurface.buoyancy(Canoe.DRAFT, 0.1, mass, Canoe.DRAFT, points), resting, "rising hull is resisted")
	assert_near(PondSurface.buoyancy(-0.1, -1.0, mass, Canoe.DRAFT, points), 0.0, 0.0, "dry probe cannot lift the boat")


func test_wave_queries_stay_inside_conservative_envelope() -> void:
	for strength: float in [0.0, 0.4, 1.5, 4.0]:
		for i in 100:
			var p := Vector2(sin(i * 1.5), cos(i * 2.6)) * 25.0
			var height := PondSurface.height_at(p, i * 0.31, WorldController.WIND_DIRECTION, strength)
			assert_true(is_finite(height), "height remains finite")
			assert_lt(absf(height - TerrainField.WATER_LEVEL), 0.012 * 2.1 * 1.3 + 0.00001, "analytic surface fits the fog envelope")


func test_physical_canoe_has_balanced_support_points() -> void:
	var moment := Vector3.ZERO
	for point in Canoe.FLOAT_POINTS:
		moment += point.cross(Vector3.UP)
	assert_lt(moment.length(), 0.00001, "equal buoyancy has no pitch or roll torque")
	var canoe := Canoe.new()
	assert_true(canoe is RigidBody3D, "canoe is a native physical body")
	assert_true(canoe.freeze, "construction does not let an unconfigured body fall")
	canoe.free()


func test_canoe_shell_has_no_open_stems_or_rim_edges() -> void:
	var canoe := Canoe.new()
	var arrays := canoe._hull_mesh().surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var triangles: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	# Caps deliberately duplicate vertices for flat normals. Weld by position
	# before checking the finished surface, so the test measures actual holes.
	var welded: Dictionary = {}
	var ids := PackedInt32Array()
	for point in vertices:
		var key := Vector3i(roundi(point.x * 1000000.0), roundi(point.y * 1000000.0), roundi(point.z * 1000000.0))
		if not welded.has(key):
			welded[key] = welded.size()
		ids.append(welded[key])
	var edges: Dictionary = {}
	for i in range(0, triangles.size(), 3):
		var a := triangles[i]
		var b := triangles[i + 1]
		var c := triangles[i + 2]
		assert_gt((vertices[b] - vertices[a]).cross(vertices[c] - vertices[a]).length_squared(), 1e-16, "shell triangles have nonzero area")
		for pair: Vector2i in [Vector2i(a, b), Vector2i(b, c), Vector2i(c, a)]:
			var first := ids[pair.x]
			var second := ids[pair.y]
			var edge := Vector2i(mini(first, second), maxi(first, second))
			edges[edge] = int(edges.get(edge, 0)) + 1
	for edge: Vector2i in edges:
		assert_eq(edges[edge], 2, "each welded shell edge joins exactly two triangles")
	canoe.free()


func test_canoe_seating_fits_the_loft_and_has_support() -> void:
	var canoe := Canoe.new()
	var mesh := MeshBuilder.new()
	canoe._add_cross_members(mesh)
	# Include face centres as well as the real board corners. The expected
	# boundary is sampled from the generated inner loft, not the seat-sizing
	# helper, so using the gunwale beam for a low seat fails this regression.
	var probes := mesh.vertices.duplicate()
	for i in range(0, mesh.indices.size(), 3):
		probes.append((mesh.vertices[mesh.indices[i]] + mesh.vertices[mesh.indices[i + 1]] + mesh.vertices[mesh.indices[i + 2]]) / 3.0)
	for point in probes:
		var width := _loft_width_at(point.y, point.z, true)
		assert_gt(width, 0.0, "cross-member stays above the floor and between the stems")
		assert_lt(absf(point.x), width - 0.001, "seat/thwart footprint stays inside the inner skin")
	var bearers := MeshBuilder.new()
	canoe._add_seat_bearers(bearers)
	assert_eq(bearers.vertex_count(), 4 * 24, "each low seat has two solid cross-bearers")
	for point in bearers.vertices:
		assert_lt(absf(point.x), _loft_width_at(point.y, point.z, false) - 0.001, "bearer joint stays behind the exterior skin")
	for face in range(0, bearers.vertex_count(), 4):
		if bearers.normals[face].dot(Vector3.UP) < 0.99:
			continue
		var p := bearers.vertices[face]
		var station := 0.5 - p.z / Canoe.LENGTH
		var seat_t := 0.15 if station < 0.5 else 0.85
		var underside := Canoe.rocker(seat_t) + Canoe.sheer(seat_t) - 0.13 - 0.015
		assert_near(p.y, underside, 1e-6, "bearer top meets seat underside")
		var left := INF
		var right := -INF
		for corner in 4:
			var support := bearers.vertices[face + corner]
			left = minf(left, support.x)
			right = maxf(right, support.x)
			assert_lt(absf(support.z - (0.5 - seat_t) * Canoe.LENGTH), 0.15, "whole bearer top lies beneath the seat footprint")
		assert_lt(left * right, 0.0, "bearer supports the seat across both sides of the centreline")
	canoe.free()


func _loft_width_at(y: float, z: float, inside: bool) -> float:
	var t := 0.5 - z / Canoe.LENGTH
	if inside:
		t = (t - Canoe.INNER_END_INSET) / (1.0 - 2.0 * Canoe.INNER_END_INSET)
	if t < 0.0 or t > 1.0:
		return 0.0
	var station := clampi(floori(t * Canoe.STATIONS), 0, Canoe.STATIONS - 1)
	var blend := t * Canoe.STATIONS - station
	var t0 := float(station) / Canoe.STATIONS
	var t1 := float(station + 1) / Canoe.STATIONS
	var previous := Canoe.inner_hull_point(t0, 0.0).lerp(Canoe.inner_hull_point(t1, 0.0), blend) if inside else Canoe.hull_point(t0, 0.0).lerp(Canoe.hull_point(t1, 0.0), blend)
	if y < previous.y:
		return 0.0
	for section in range(Canoe.SECTIONS / 2 + 1, Canoe.SECTIONS + 1):
		var u := 2.0 * float(section) / Canoe.SECTIONS - 1.0
		var next := Canoe.inner_hull_point(t0, u).lerp(Canoe.inner_hull_point(t1, u), blend) if inside else Canoe.hull_point(t0, u).lerp(Canoe.hull_point(t1, u), blend)
		if y <= next.y and next.y > previous.y:
			return lerpf(previous.x, next.x, (y - previous.y) / (next.y - previous.y))
		previous = next
	return previous.x
