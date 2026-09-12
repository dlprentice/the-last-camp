class_name Canoe
extends RigidBody3D

## A cedar-strip canoe: a lofted hull with rocker and sheer, gunwale rails,
## thwarts, timber seats and a paddle across the thwarts. It floats on the pond
## using six buoyancy samples, water drag and a compliant mooring. The hull's local origin is the keel line at the
## waterline, so `position.y` is the water level minus the draft.

const LENGTH := 4.6
const HALF_BEAM := 0.43
const DEPTH := 0.34
const DRAFT := 0.11
## Inner floor (ribs and slats) sits above the waterline so the open hull
## never shows the pond surface inside it.
const FLOOR_HEIGHT := DRAFT + 0.045
const STATIONS := 96
const SECTIONS := 24
const INNER_END_INSET := 0.018
const INNER_WIDTH_SCALE := 0.94
const GUNWALE_OFFSET := 0.004
const CROSS_MEMBERS := [
	Vector3(0.30, 0.08, 0.0), Vector3(0.50, 0.11, 0.0), Vector3(0.70, 0.08, 0.0),
	Vector3(0.15, 0.30, -0.13), Vector3(0.85, 0.30, -0.13),
]

const FLOATING_MASS := 55.0
const FLOAT_POINTS: Array[Vector3] = [
	Vector3(-0.27, 0, -1.30), Vector3(0.27, 0, -1.30),
	Vector3(-0.32, 0, 0), Vector3(0.32, 0, 0),
	Vector3(-0.27, 0, 1.30), Vector3(0.27, 0, 1.30),
]
var pond: Pond
var _moored_position := Vector3.ZERO
var _moored_yaw := 0.0
var _probe_heights := PackedFloat32Array()


func _init() -> void:
	name = "Canoe"
	freeze = true


func build(hull_tint: Color) -> void:
	var hull_mat := PropMaterials.wood(hull_tint, 0.05, 0.45, 0.7)
	hull_mat.set_shader_parameter("use_shore_mask", true)
	var trim_mat := PropMaterials.wood(Color(0.55, 0.38, 0.24), 0.15, 0.0, 0.85)
	trim_mat.set_shader_parameter("wet_band", 0.08)
	var hull := MeshInstance3D.new()
	hull.name = "Hull"
	hull.mesh = _hull_mesh()
	hull.material_override = hull_mat
	hull.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(hull)

	var trim := MeshInstance3D.new()
	trim.name = "Trim"
	trim.mesh = _trim_mesh()
	trim.material_override = trim_mat
	trim.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(trim)
	var paddle_mat := PropMaterials.wood(Color(0.55, 0.38, 0.24), 0.05, 0.0, 1.05)
	paddle_mat.set_shader_parameter("normal_strength", 0.22)
	paddle_mat.set_shader_parameter("wet_band", 0.08)
	var paddle := MeshInstance3D.new()
	paddle.name = "Paddle"
	var paddle_mesh := MeshBuilder.new()
	_add_paddle(paddle_mesh)
	paddle.mesh = paddle_mesh.commit()
	paddle.material_override = paddle_mat
	paddle.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(paddle)

	collision_layer = 1
	collision_mask = 1
	set_meta("surface", &"wood")
	mass = FLOATING_MASS
	can_sleep = false
	linear_damp = 0.08
	angular_damp = 0.35
	center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	center_of_mass = Vector3(0, 0.04, 0)
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(HALF_BEAM * 2.0, DEPTH + 0.1, LENGTH * 0.96)
	shape.shape = box
	shape.position = Vector3(0.0, DEPTH * 0.5, 0.0)
	add_child(shape)
	_moored_position = global_position
	_moored_yaw = global_rotation.y


## Height of the sheer line (gunwale) above the keel line along the hull.
static func sheer(t: float) -> float:
	return DEPTH + 0.15 * pow(1.0 - sin(t * PI), 1.6)


## Keel rocker: the ends of the keel lift clear of the water.
static func rocker(t: float) -> float:
	return 0.07 * pow(1.0 - sin(t * PI), 1.4)


static func half_width(t: float) -> float:
	return maxf(HALF_BEAM * pow(sin(t * PI), 0.62), 0.012)


## Hull surface point. `t` runs stern (0) to bow (1) along -Z; `u` runs from
## the port gunwale (-1) around the keel to the starboard gunwale (+1).
static func hull_point(t: float, u: float) -> Vector3:
	var a := u * PI * 0.5
	var x := half_width(t) * sin(a)
	var y := rocker(t) + sheer(t) * (1.0 - cos(a))
	var z := (0.5 - t) * LENGTH
	return Vector3(x, y, z)


static func hull_outward(u: float) -> Vector3:
	var a := u * PI * 0.5
	return Vector3(sin(a), -cos(a), 0.0)


## The interior stops short of each solid stem. Its raised floor retains the
## water exclusion of the original hull without leaving a hole at either end.
static func inner_hull_point(t: float, u: float) -> Vector3:
	var station := lerpf(INNER_END_INSET, 1.0 - INNER_END_INSET, t)
	var p := hull_point(station, u)
	p.x *= INNER_WIDTH_SCALE
	p.y = maxf(p.y, FLOOR_HEIGHT + rocker(station))
	return p


static func interior_half_width(t: float, y: float) -> float:
	if t < INNER_END_INSET or t > 1.0 - INNER_END_INSET or y < FLOOR_HEIGHT + rocker(t):
		return 0.0
	var cosine := 1.0 - clampf((y - rocker(t)) / sheer(t), 0.0, 1.0)
	return half_width(t) * INNER_WIDTH_SCALE * sqrt(maxf(0.0, 1.0 - cosine * cosine))


static func _skin_normal(t: float, u: float, inner: bool) -> Vector3:
	var along: Vector3
	var across: Vector3
	if inner:
		along = inner_hull_point(minf(t + 0.001, 1.0), u) - inner_hull_point(maxf(t - 0.001, 0.0), u)
		across = inner_hull_point(t, minf(u + 0.001, 1.0)) - inner_hull_point(t, maxf(u - 0.001, -1.0))
	else:
		along = hull_point(minf(t + 0.001, 1.0), u) - hull_point(maxf(t - 0.001, 0.0), u)
		across = hull_point(t, minf(u + 0.001, 1.0)) - hull_point(t, maxf(u - 0.001, -1.0))
	var expected := -hull_outward(u) if inner else hull_outward(u)
	var n := across.cross(along)
	if n.length_squared() < 1e-10:
		return expected
	if n.dot(expected) < 0.0:
		n = -n
	return n.normalized()


func _hull_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var rows: Array[PackedInt32Array] = []
	for i in range(STATIONS + 1):
		var t := float(i) / float(STATIONS)
		var row := PackedInt32Array()
		for j in range(SECTIONS + 1):
			var u := float(j) / float(SECTIONS) * 2.0 - 1.0
			var p := hull_point(t, u)
			var n := _skin_normal(t, u, false)
			# Strakes run along the hull: v follows the length, u wraps the girth.
			var uv := Vector2((u + 1.0) * 1.0, t * LENGTH)
			var tangent := MeshBuilder.tangent_for(n, Vector3.RIGHT, Vector3.FORWARD)
			var shade := Color(1.0, 1.0, 1.0).lerp(Color(0.86, 0.8, 0.72), 0.5 - 0.5 * cos(float(j) * 1.7))
			row.append(mb.add_vertex(p, n, uv, shade, tangent))
		rows.append(row)
	for i in STATIONS:
		for j in SECTIONS:
			var a := rows[i][j]
			var b := rows[i][j + 1]
			var c := rows[i + 1][j + 1]
			var d := rows[i + 1][j]
			var u := (float(j) + 0.5) / float(SECTIONS) * 2.0 - 1.0
			mb.add_quad_facing(a, b, c, d, hull_outward(u))
	# Inner skin: the same loft pulled inwards with its bottom flattened into
	# a floor above the waterline.
	var inner: Array[PackedInt32Array] = []
	for i in range(STATIONS + 1):
		var t := float(i) / float(STATIONS)
		var row := PackedInt32Array()
		for j in range(SECTIONS + 1):
			var u := float(j) / float(SECTIONS) * 2.0 - 1.0
			var p := inner_hull_point(t, u)
			var n := _skin_normal(t, u, true)
			var uv := Vector2((u + 1.0) * 1.0, t * LENGTH)
			var tangent := MeshBuilder.tangent_for(n, Vector3.RIGHT, Vector3.FORWARD)
			row.append(mb.add_vertex(p, n, uv, Color(0.9, 0.86, 0.8, 0.0), tangent))
		inner.append(row)
	for i in STATIONS:
		for j in SECTIONS:
			var u := (float(j) + 0.5) / float(SECTIONS) * 2.0 - 1.0
			mb.add_quad_facing(inner[i][j], inner[i][j + 1], inner[i + 1][j + 1], inner[i + 1][j], -hull_outward(u) + Vector3(0.0, 0.5, 0.0))
	# The outer/inner skins are one closed shell. End bulkheads, short stem
	# decks and the lip under each gunwale all share exact boundary positions.
	for i in STATIONS:
		for edge: int in [0, SECTIONS]:
			mb.add_quad_facing(rows[i][edge], rows[i + 1][edge], inner[i + 1][edge], inner[i][edge], Vector3.UP)
	for end: int in [0, STATIONS]:
		var outward := Vector3.BACK if end == 0 else Vector3.FORWARD
		_add_end_cap(mb, rows[end], outward, Color.WHITE)
		_add_end_cap(mb, inner[end], -outward, Color(0.9, 0.86, 0.8, 0.0))
		mb.add_quad_facing(rows[end][0], rows[end][SECTIONS], inner[end][SECTIONS], inner[end][0], Vector3.UP)
	mb.recompute_tangents()
	# A single hero prop does not need automatic simplification of its narrow
	# sealed lip or stems; keep those connections at every camera distance.
	return mb.commit()


func _add_end_cap(mb: MeshBuilder, row: PackedInt32Array, normal: Vector3, tint: Color) -> void:
	var centre := Vector3.ZERO
	for index in row:
		centre += mb.vertices[index]
	centre /= float(row.size())
	var middle := mb.add_vertex(centre, normal, Vector2(0.37, centre.y), tint)
	var rim := PackedInt32Array()
	for index in row:
		var p := mb.vertices[index]
		rim.append(mb.add_vertex(p, normal, Vector2(0.37 + p.x, p.y), tint))
	for j in rim.size():
		var a := rim[j]
		var b := rim[(j + 1) % rim.size()]
		if (mb.vertices[a] - centre).cross(mb.vertices[b] - centre).dot(normal) > 0.0:
			mb.add_triangle(middle, b, a)
		else:
			mb.add_triangle(middle, a, b)


func _trim_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var gunwale := _gunwale_loop()
	var rail_widths: Array[float] = []
	var rail_heights: Array[float] = []
	for point in gunwale:
		rail_widths.append(0.010)
		rail_heights.append(0.018)
	_add_trim_sweep(mb, gunwale, rail_widths, rail_heights, true, 1, Color.WHITE)
	_add_cross_members(mb)
	_add_seat_bearers(mb)
	mb.recompute_tangents()
	return mb.commit()


func _gunwale_loop() -> Array[Vector3]:
	var points: Array[Vector3] = []
	for i in STATIONS + 1:
		var t := float(i) / float(STATIONS)
		points.append(hull_point(t, -1.0) + Vector3(-GUNWALE_OFFSET, 0.008, 0.0))
	# Rounded returns join the rails over both solid stems. The return radius
	# exceeds the rail's horizontal half-width, avoiding a pinched/self-cut tip.
	var radius := half_width(1.0) + GUNWALE_OFFSET
	var tip_y := rocker(1.0) + sheer(1.0) + 0.008
	for i in range(1, 13):
		var angle := PI * (1.0 - float(i) / 12.0)
		points.append(Vector3(radius * cos(angle), tip_y, -LENGTH * 0.5 - radius * sin(angle)))
	for i in range(STATIONS - 1, -1, -1):
		var t := float(i) / float(STATIONS)
		points.append(hull_point(t, 1.0) + Vector3(GUNWALE_OFFSET, 0.008, 0.0))
	for i in range(1, 12):
		var angle := PI * float(i) / 12.0
		points.append(Vector3(radius * cos(angle), tip_y, LENGTH * 0.5 + radius * sin(angle)))
	return points


## A closed rail or capped paddle part with one shared ring per station.
## The world-up frame is well-conditioned on these nearly horizontal paths;
## it is evaluated consistently at the closing ring rather than restarted.
func _add_trim_sweep(mb: MeshBuilder, points: Array[Vector3], widths: Array[float], heights: Array[float],
		closed: bool, strip: int, tint: Color) -> void:
	const SIDES := 16
	var count := points.size()
	var lengths := PackedFloat32Array([0.0])
	for i in range(1, count):
		lengths.append(lengths[i - 1] + points[i].distance_to(points[i - 1]))
	var total := lengths[count - 1] + (points[count - 1].distance_to(points[0]) if closed else 0.0)
	var v_scale := maxf(1.0, roundf(total)) / total if closed else 1.0
	var base := mb.vertex_count()
	var frames: Array[Vector3] = []
	for i in count + (1 if closed else 0):
		var index := i % count
		var before := (index - 1 + count) % count if closed else maxi(index - 1, 0)
		var after := (index + 1) % count if closed else mini(index + 1, count - 1)
		var along := (points[after] - points[before]).normalized()
		var side := Vector3.UP.cross(along).normalized()
		var up := along.cross(side).normalized()
		frames.append(along)
		var v := total * v_scale if i == count else lengths[index] * v_scale
		for j in SIDES + 1:
			var a := TAU * float(j) / float(SIDES)
			var p := points[index] + side * cos(a) * widths[index] + up * sin(a) * heights[index]
			var n := (side * cos(a) / widths[index] + up * sin(a) / heights[index]).normalized()
			var across := -side * sin(a) * widths[index] + up * cos(a) * heights[index]
			var uv := Vector2(float(posmod(strip, 4)) * 0.25 + 0.018 + 0.214 * float(j) / SIDES, v)
			mb.add_vertex(p, n, uv, tint, MeshBuilder.tangent_for(n, across, along))
	for i in count if closed else count - 1:
		for j in SIDES:
			var a := base + i * (SIDES + 1) + j
			var b := a + 1
			var c := b + SIDES + 1
			var d := a + SIDES + 1
			mb.add_quad_facing(a, b, c, d, mb.normals[a] + mb.normals[b])
	if not closed:
		for end: int in [0, count - 1]:
			var ring := PackedInt32Array()
			for j in SIDES:
				ring.append(base + end * (SIDES + 1) + j)
			_add_end_cap(mb, ring, frames[end] * (-1.0 if end == 0 else 1.0), tint)


func _add_cross_members(mb: MeshBuilder) -> void:
	# Thwarts and the centre yoke, seats near the ends.
	for spec: Vector3 in CROSS_MEMBERS:
		var t := spec.x
		var width := spec.y
		var drop := spec.z
		var y := rocker(t) + sheer(t) + drop
		# The lower corners, including the edge nearest the narrow stem, must
		# fit inside the loft. Gunwale beam is not the usable width of a low seat.
		var half_span := HALF_BEAM
		for sample in 9:
			var station := t + lerpf(-width * 0.5, width * 0.5, float(sample) / 8.0) / LENGTH
			half_span = minf(half_span, interior_half_width(station, y - 0.015))
		var span := 2.0 * maxf(0.0, half_span - 0.004)
		var xf := Transform3D(Basis(Vector3.UP, PI * 0.5), Vector3(0.0, y, (0.5 - t) * LENGTH))
		PropMeshes.add_board(mb, xf, Vector3(width, 0.03, span), 2, t * 3.0, Color(0.95, 0.93, 0.9))


func _add_seat_bearers(mb: MeshBuilder) -> void:
	# Two narrow cross-bearers meet each low seat's underside. Their bevelled
	# ends follow the inside skin with a 2 mm housed joint, while preserving
	# at least 3 mm of clearance from the outside of the hull.
	for seat: Vector3 in CROSS_MEMBERS:
		if seat.z >= 0.0:
			continue
		var seat_bottom := rocker(seat.x) + sheer(seat.x) + seat.z - 0.015
		for offset: float in [-0.105, 0.105]:
			var station := seat.x + offset / LENGTH
			var y := seat_bottom - 0.013
			var span := 2.0 * interior_half_width(station, y)
			var xf := Transform3D(Basis(Vector3.UP, PI * 0.5), Vector3(0.0, y, (0.5 - station) * LENGTH))
			var first := mb.vertex_count()
			PropMeshes.add_board(mb, xf, Vector3(0.028, 0.026, span), 2, station * 3.0, Color(0.95, 0.93, 0.9))
			for i in range(first, mb.vertex_count()):
				var p := mb.vertices[i]
				var t := 0.5 - p.z / LENGTH
				var inner_span := interior_half_width(t, p.y)
				var fitted := minf(inner_span + 0.002, inner_span / INNER_WIDTH_SCALE - 0.003)
				mb.vertices[i].x = signf(p.x) * fitted
			# add_board has separate vertices for each face; retain crisp joinery
			# normals after beveling the end corners to the curved skin.
			for i in range(first, mb.vertex_count(), 4):
				var n := (mb.vertices[i + 1] - mb.vertices[i]).cross(mb.vertices[i + 3] - mb.vertices[i]).normalized()
				for corner in 4:
					mb.normals[i + corner] = n


func _add_paddle(mb: MeshBuilder) -> void:
	# The paddle rests on the centre yoke and forward thwart. Its blade has a
	# rounded shoulder and thin oval edge; the hand grip is a rounded T, not a box.
	var centre_height := rocker(0.5) + sheer(0.5) + 0.015 + 0.016
	var forward_height := rocker(0.7) + sheer(0.7) + 0.015 + 0.009
	var rise := (forward_height - centre_height) / (LENGTH * 0.2)
	var grip := Vector3(-0.32, centre_height - 0.55 * rise, 0.55)
	var tip := Vector3(0.34, centre_height + 0.95 * rise, -0.95)
	var shaft_dir := (tip - grip).normalized()
	var blade_start := grip.lerp(tip, 0.65)
	var tint := Color(0.9, 0.8, 0.62)
	_add_trim_sweep(mb, [grip, blade_start + shaft_dir * 0.04], [0.016, 0.014], [0.016, 0.014], false, 3, tint)
	var blade_points: Array[Vector3] = []
	var widths: Array[float] = []
	var heights: Array[float] = []
	for shape: Vector3 in [Vector3(0.0, 0.016, 0.013), Vector3(0.13, 0.045, 0.012),
			Vector3(0.32, 0.075, 0.011), Vector3(0.66, 0.085, 0.010),
			Vector3(0.90, 0.070, 0.009), Vector3(0.985, 0.033, 0.007), Vector3(1.0, 0.005, 0.004)]:
		blade_points.append(blade_start.lerp(tip, shape.x))
		widths.append(shape.y)
		heights.append(shape.z)
	_add_trim_sweep(mb, blade_points, widths, heights, false, 3, tint)
	var across := Vector3.UP.cross(shaft_dir).normalized()
	var grip_points: Array[Vector3] = []
	for offset: float in [-0.05, -0.042, 0.0, 0.042, 0.05]:
		grip_points.append(grip + across * offset)
	_add_trim_sweep(mb, grip_points, [0.003, 0.019, 0.021, 0.019, 0.003],
		[0.003, 0.013, 0.014, 0.013, 0.003], false, 3, tint)


## World-space point at the bow (the -Z end) for the mooring line.
func bow_point() -> Vector3:
	return to_global(Vector3(0.0, rocker(1.0) + sheer(1.0) - 0.04, -LENGTH * 0.5 + 0.06))


func start_floating(on_pond: Pond) -> void:
	pond = on_pond
	_probe_heights.resize(FLOAT_POINTS.size())
	_probe_heights.fill(0.0)
	freeze = false


func probe_positions() -> PackedVector2Array:
	var positions := PackedVector2Array()
	for point in FLOAT_POINTS:
		var at := to_global(point)
		positions.append(Vector2(at.x, at.z))
	return positions


func set_residual_heights(heights: PackedFloat32Array) -> void:
	if heights.size() == FLOAT_POINTS.size():
		_probe_heights = heights


func _integrate_forces(state: PhysicsDirectBodyState3D) -> void:
	if pond == null:
		return
	var pose := state.transform
	var wind := WorldController.WIND_DIRECTION.normalized()
	var strength: float = Game.world.wind_strength() if Game.world != null else 0.4
	for i in FLOAT_POINTS.size():
		var offset := pose.basis * FLOAT_POINTS[i]
		var at := pose.origin + offset
		var water := pond.base_height(Vector2(at.x, at.z)) + _probe_heights[i]
		var immersion := water - at.y
		var point_velocity := state.linear_velocity + state.angular_velocity.cross(offset - pose.basis * center_of_mass)
		var upward := PondSurface.buoyancy(immersion, point_velocity.y, mass, DRAFT, FLOAT_POINTS.size())
		var wet := clampf(immersion / DRAFT, 0.0, 1.0)
		var horizontal := Vector3(point_velocity.x, 0, point_velocity.z)
		var drag := -horizontal * (6.0 + horizontal.length() * 24.0) * wet
		state.apply_force(Vector3.UP * upward + drag, offset)
	# The mooring/fenders resist horizontal drift and yaw but leave heave,
	# pitch and roll free. Forces are integrated by Jolt, never assigned poses.
	var drift := pose.origin - _moored_position
	drift.y = 0.0
	var velocity := Vector3(state.linear_velocity.x, 0, state.linear_velocity.z)
	state.apply_central_force(-drift * 45.0 - velocity * 22.0 + Vector3(wind.x, 0, wind.y) * strength * 2.0)
	var heading := pose.basis.get_euler().y
	state.apply_torque(Vector3.UP * (-wrapf(heading - _moored_yaw, -PI, PI) * 28.0 - state.angular_velocity.y * 16.0))
