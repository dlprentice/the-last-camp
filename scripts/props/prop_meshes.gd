class_name PropMeshes
extends RefCounted

## Shared procedural meshes for camp props. Deterministic for a given seed so
## captures and tests stay stable.

const WOOD_SIDES := 8


static func log_mesh(length: float, radius: float, seed_value: int, bend := 0.08) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var mb := MeshBuilder.new()
	var half := length * 0.5
	var lean := Vector3(rng.randf_range(-1.0, 1.0), 0.0, rng.randf_range(-1.0, 1.0))
	if lean.length_squared() < 1e-6:
		lean = Vector3.RIGHT
	lean = lean.normalized() * bend * length
	var points: Array[Vector3] = [
		Vector3(-half, 0.0, 0.0),
		Vector3(0.0, lean.y + rng.randf_range(-0.02, 0.04), lean.z * 0.5),
		Vector3(half, 0.0, 0.0),
	]
	var radii: Array[float] = [
		radius * rng.randf_range(0.92, 1.08),
		radius * rng.randf_range(0.88, 1.0),
		radius * rng.randf_range(0.9, 1.06),
	]
	mb.add_tube(points, radii, WOOD_SIDES, Color.WHITE, 1.0, 1.4, rng.randf(), true)
	return mb.commit(null, true)


## Round timber with bark on the outside and sawn end grain: two surfaces,
## bark first (bark shader, v along the log) then the ends (wood shader with
## concentric-ring UVs). `flat_top` < 1 hews the upper side flat for a seat.
static func bark_log_mesh(length: float, radius: float, seed_value: int, bend := 0.05, flat_top := 1.0) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var half := length * 0.5
	var flat_y := radius * flat_top
	var bark := MeshBuilder.new()
	var segments := 5
	var sides := 12
	var points: Array[Vector3] = []
	var radii: Array[float] = []
	var lean := Vector3(0.0, rng.randf_range(-1.0, 1.0), rng.randf_range(-1.0, 1.0)).normalized() * bend * length
	for i in range(segments + 1):
		var t := float(i) / float(segments)
		points.append(Vector3(-half + length * t, 0.0, 0.0) + lean * sin(t * PI))
		radii.append(radius * rng.randf_range(0.93, 1.07))
	bark.add_tube(points, radii, sides, Color.WHITE, 1.0, 1.0 / 1.2, rng.randf(), false)
	if flat_top < 1.0:
		for i in bark.vertices.size():
			var v := bark.vertices[i]
			if v.y > flat_y:
				bark.vertices[i] = Vector3(v.x, flat_y, v.z)
		bark.recompute_normals()
	var mesh := bark.commit(null, true)

	var ends := MeshBuilder.new()
	if flat_top < 1.0:
		# Sawn face along the top, a hair above the clipped bark.
		var chord := radius * sqrt(maxf(1.0 - flat_top * flat_top, 0.0)) * 0.98
		var strip := rng.randi() % 4
		var u0 := float(strip) * 0.25 + 0.02
		var y := flat_y + 0.004
		var quads := 6
		for q in quads:
			var t0 := float(q) / float(quads)
			var t1 := float(q + 1) / float(quads)
			var p0 := points[0].lerp(points[points.size() - 1], t0)
			var p1 := points[0].lerp(points[points.size() - 1], t1)
			ends.add_quad(Vector3(p0.x, y, -chord), Vector3(p1.x, y, -chord), Vector3(p1.x, y, chord), Vector3(p0.x, y, chord),
					Color(0.98, 0.92, 0.8), Color(0, 0, 0, 0), Rect2(u0 + 0.02, t0 * length, 0.18, (t1 - t0) * length))
	for side: float in [-1.0, 1.0]:
		var centre := points[0] if side < 0.0 else points[points.size() - 1]
		var r := radii[0] if side < 0.0 else radii[radii.size() - 1]
		var n := Vector3(side, 0.0, 0.0)
		var strip := rng.randi() % 4
		var u0 := float(strip) * 0.25 + 0.02
		var ci := ends.add_vertex(centre, n, Vector2(u0, 0.0), Color(0.95, 0.9, 0.82), Vector4(0.0, 0.0, side, 1.0))
		var rim: Array[int] = []
		for s in range(sides + 1):
			var a := float(s) / float(sides) * TAU
			var p := centre + Vector3(0.0, minf(sin(a) * r, flat_y), cos(a) * r)
			var ring := (p - centre).length() / r
			rim.append(ends.add_vertex(p, n, Vector2(u0 + ring * 0.2, a / TAU * 3.0), Color(0.95, 0.9, 0.82), Vector4(0.0, 0.0, side, 1.0)))
		for s in sides:
			if side < 0.0:
				ends.add_triangle(ci, rim[s], rim[s + 1])
			else:
				ends.add_triangle(ci, rim[s + 1], rim[s])
	ends.commit(null, false, mesh)
	return mesh


## Hand-split firewood: an irregular bark sector and two fractured inner
## faces. Every surface shares the same ring positions, including the ends.
## The grain runs along local X; the pointed split edge faces approximately +Y.
static func split_log_mesh(length: float, radius: float, seed_value: int) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var half := length * 0.5
	const SIDES := 6
	const RINGS := 5
	var arc := rng.randf_range(2.02, 2.65)
	var taper := rng.randf_range(-0.09, 0.09)
	var split_offset := rng.randf_range(-0.14, 0.14)
	var bow := Vector2(rng.randf_range(-0.035, 0.035), rng.randf_range(-0.035, 0.035)) * radius
	var profiles: Array[PackedVector3Array] = []
	for ring in RINGS:
		var t := float(ring) / float(RINGS - 1)
		var centre := Vector3(lerpf(-half, half, t), bow.x * sin(t * PI), bow.y * sin(t * PI))
		var r := radius * (1.0 + taper * (t - 0.5)) * rng.randf_range(0.97, 1.03)
		var points := PackedVector3Array()
		for s in SIDES + 1:
			var angle := PI * 1.5 + (float(s) / SIDES - 0.5) * arc
			var irregular := 1.0 if s == 0 or s == SIDES else rng.randf_range(0.97, 1.03)
			points.append(centre + Vector3(0.0, sin(angle) * r * irregular, cos(angle) * r * irregular))
		# A slightly wandering cleft makes two split faces, not one broad shelf.
		points.append(centre + Vector3(0.0, r * rng.randf_range(0.04, 0.13), r * split_offset))
		profiles.append(points)
	var bark := MeshBuilder.new()
	var wood := MeshBuilder.new()
	var strip := rng.randi() % 4
	var u0 := float(strip) * 0.25 + 0.02
	var v0 := rng.randf()
	var timber_tint := Color(0.98, 0.94, 0.86) * rng.randf_range(0.88, 1.03)
	for ring in RINGS - 1:
		var a := profiles[ring]
		var b := profiles[ring + 1]
		var along := float(ring) / float(RINGS - 1) * length
		var run := length / float(RINGS - 1)
		for s in SIDES:
			_firewood_quad(bark, a[s], b[s], b[s + 1], a[s + 1], Color.WHITE,
				Rect2(float(s) / SIDES * 0.45, along / 1.2, 0.45 / SIDES, run / 1.2))
		# Preserve the same endpoints as the bark and cap; no radius mismatch
		# can leave a hairline opening along the edge of a split piece.
		_firewood_quad(wood, a[SIDES + 1], b[SIDES + 1], b[0], a[0], timber_tint, Rect2(u0, v0 + along, 0.105, run))
		_firewood_quad(wood, a[SIDES], b[SIDES], b[SIDES + 1], a[SIDES + 1], timber_tint, Rect2(u0 + 0.105, v0 + along, 0.105, run))
	for end in 2:
		var points := profiles[0 if end == 0 else RINGS - 1]
		var centre := Vector3.ZERO
		for point in points:
			centre += point
		centre /= float(points.size())
		var side := -1.0 if end == 0 else 1.0
		var n := Vector3(side, 0.0, 0.0)
		var ci := wood.add_vertex(centre, n, Vector2(u0, 0.0), timber_tint, Vector4(0.0, 0.0, side, 1.0))
		var rim: Array[int] = []
		for point in points:
			var direction := Vector2(point.z - centre.z, point.y - centre.y)
			rim.append(wood.add_vertex(point, n, Vector2(u0 + minf(direction.length() / radius, 1.0) * 0.2, atan2(direction.y, direction.x) / TAU * 3.0),
				timber_tint, Vector4(0.0, 0.0, side, 1.0)))
		for s in rim.size():
			var next := (s + 1) % rim.size()
			if (points[s] - centre).cross(points[next] - centre).dot(n) < 0.0:
				wood.add_triangle(ci, rim[s], rim[next])
			else:
				wood.add_triangle(ci, rim[next], rim[s])
	bark.recompute_tangents()
	wood.recompute_tangents()
	var mesh := bark.commit(null, false)
	wood.commit(null, false, mesh)
	return mesh


## These strips run lengthwise along p0-p1: V follows the scanned wood grain.
static func _firewood_quad(builder: MeshBuilder, a: Vector3, b: Vector3, c: Vector3, d: Vector3, tint: Color, uv: Rect2) -> void:
	var first := builder.vertex_count()
	builder.add_quad(a, b, c, d, tint)
	builder.uvs[first] = uv.position
	builder.uvs[first + 1] = Vector2(uv.position.x, uv.end.y)
	builder.uvs[first + 2] = uv.end
	builder.uvs[first + 3] = Vector2(uv.end.x, uv.position.y)


## A chopping block: a short upright round with ringed top grain.
static func stump_mesh(radius: float, height: float, seed_value: int) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var bark := MeshBuilder.new()
	var sides := 14
	bark.add_tube([Vector3(0.0, -0.1, 0.0), Vector3(0.0, height * 0.5, 0.0), Vector3(0.0, height, 0.0)],
			[radius * 1.08, radius, radius * 0.97], sides, Color.WHITE, 1.0, 1.0 / 1.2, rng.randf(), false)
	var mesh := bark.commit(null, true)
	var top := MeshBuilder.new()
	var u0 := float(rng.randi() % 4) * 0.25 + 0.02
	var centre := Vector3(0.0, height, 0.0)
	var ci := top.add_vertex(centre, Vector3.UP, Vector2(u0, 0.0), Color(0.95, 0.9, 0.82), Vector4(1.0, 0.0, 0.0, 1.0))
	var rim: Array[int] = []
	for s in range(sides + 1):
		var a := float(s) / float(sides) * TAU
		rim.append(top.add_vertex(centre + Vector3(cos(a) * radius * 0.97, 0.0, sin(a) * radius * 0.97), Vector3.UP,
				Vector2(u0 + 0.2, a / TAU * 4.0), Color(0.95, 0.9, 0.82), Vector4(1.0, 0.0, 0.0, 1.0)))
	for s in sides:
		top.add_triangle(ci, rim[s], rim[s + 1])
	top.commit(null, false, mesh)
	return mesh


## A felling axe: hickory handle and a wedge head, origin at the head's edge.
static func axe_mesh() -> ArrayMesh:
	var handle := MeshBuilder.new()
	var points: Array[Vector3] = []
	var radii: Array[float] = []
	for i in 19:
		var t := float(i) / 18.0
		points.append(Vector3(0, t * 0.72, sin(t * PI) * 0.018 - t * t * 0.022))
		radii.append(0.016 + sin(t * PI) * 0.0025 + pow(t, 8.0) * 0.006)
	handle.add_tube(points, radii, 14, Color(0.92, 0.85, 0.7), 1, 0.8, 0, true)
	for i in handle.vertex_count():
		handle.vertices[i].x *= 0.78
	var mesh := handle.commit(null, true)
	var head := MeshBuilder.new()
	# Curved cutting edge, narrowed cheek and poll around the handle eye.
	# The silhouette is extruded with a thickness taper towards the bit.
	var outline := PackedVector2Array([
		Vector2(-0.045, -0.007), Vector2(-0.045, 0.065), Vector2(0.005, 0.064),
		Vector2(0.07, 0.079), Vector2(0.125, 0.108), Vector2(0.148, 0.074),
		Vector2(0.156, 0.034), Vector2(0.148, -0.007), Vector2(0.13, -0.034), Vector2(0.06, -0.002)])
	var triangles := Geometry2D.triangulate_polygon(outline)
	for side: float in [-1.0, 1.0]:
		var start := head.vertex_count()
		for p in outline:
			var thickness := lerpf(0.024, 0.0012, smoothstep(0.015, 0.145, p.x))
			var color := Color(0.35, 0.37, 0.38).lerp(Color(0.83, 0.85, 0.84), smoothstep(0.105, 0.145, p.x))
			head.add_vertex(Vector3(side * thickness, p.y, p.x), Vector3.RIGHT * side, p * 4.0, color)
		for i in range(0, triangles.size(), 3):
			var x := start + triangles[i]
			var y := start + triangles[i + 1]
			var z := start + triangles[i + 2]
			if (head.vertices[y] - head.vertices[x]).cross(head.vertices[z] - head.vertices[x]).x * side > 0:
				head.add_triangle(x, z, y)
			else:
				head.add_triangle(x, y, z)
	for i in outline.size():
		var next := (i + 1) % outline.size()
		var n := Vector3(0, outline[next].x - outline[i].x, outline[i].y - outline[next].y).normalized()
		head.add_quad_facing(i, next, next + outline.size(), i + outline.size(), n)
	head.recompute_normals()
	head.commit(null, false, mesh)
	return mesh


## Metal parts of a hurricane lantern (base, tank, cage, cap, bail), origin at
## the bottom of the base. The bail's top sits at HURRICANE_HEIGHT.
const HURRICANE_HEIGHT := 0.5

static func hurricane_metal() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var grey := Color(0.9, 0.9, 0.9)
	var dark := Color(0.55, 0.52, 0.5)
	# Fuel font: a squat can with a domed shoulder, a filler cap on one side.
	mb.add_tube([Vector3(0.0, 0.0, 0.0), Vector3(0.0, 0.012, 0.0), Vector3(0.0, 0.06, 0.0), Vector3(0.0, 0.095, 0.0), Vector3(0.0, 0.108, 0.0)],
			[0.06, 0.066, 0.066, 0.05, 0.03], 16, grey, 1.0, 2.0, 0.0, true)
	mb.add_tube([Vector3(0.042, 0.092, 0.0), Vector3(0.042, 0.106, 0.0)], [0.011, 0.012], 8, grey, 1.0, 2.0, 0.0, true)
	# Burner collar and the wick tube inside the globe, with the raiser knob.
	mb.add_tube([Vector3(0.0, 0.108, 0.0), Vector3(0.0, 0.126, 0.0)], [0.03, 0.028], 10, grey, 1.0, 2.0, 0.0, true)
	mb.add_tube([Vector3(0.0, 0.126, 0.0), Vector3(0.0, 0.15, 0.0), Vector3(0.0, 0.172, 0.0)], [0.018, 0.014, 0.007], 8, dark, 1.0, 2.0, 0.0, true)
	mb.add_tube([Vector3(0.028, 0.118, 0.0), Vector3(0.05, 0.118, 0.0)], [0.004, 0.004], 5, grey, 1.0, 2.0, 0.0, true)
	mb.add_tube([Vector3(0.05, 0.118, 0.0), Vector3(0.058, 0.118, 0.0)], [0.012, 0.012], 8, grey, 1.0, 2.0, 0.0, true)
	# Side air tubes: the tubular lantern's signature, rising from the font's
	# shoulder and curving in to feed the cap.
	for side: float in [-1.0, 1.0]:
		var foot := Vector3(side * 0.06, 0.07, 0.0)
		var mid := Vector3(side * 0.078, 0.2, 0.0)
		var top := Vector3(side * 0.05, 0.315, 0.0)
		mb.add_tube([foot, Vector3(side * 0.077, 0.13, 0.0), mid, Vector3(side * 0.07, 0.275, 0.0), top],
				[0.012, 0.012, 0.012, 0.011, 0.01], 8, grey, 1.0, 4.0, 0.0, true)
	# Wire guard: two rings around the globe joined by thin uprights fore and aft.
	for spec: Array in [[0.15, 0.067], [0.245, 0.064]]:
		var ring: Array[Vector3] = []
		var ring_r: Array[float] = []
		for k in 21:
			var a := float(k) / 20.0 * TAU
			ring.append(Vector3(cos(a) * spec[1], spec[0], sin(a) * spec[1]))
			ring_r.append(0.0032)
		mb.add_tube(ring, ring_r, 5, grey, 1.0, 4.0, 0.0, false)
	for side: float in [-1.0, 1.0]:
		mb.add_tube([Vector3(0.0, 0.118, side * 0.06), Vector3(0.0, 0.29, side * 0.062)], [0.0032, 0.0032], 5, grey, 1.0, 4.0, 0.0, true)
	# Cap: a vented crown with a dark slot band under the brim, and the chimney.
	mb.add_tube([Vector3(0.0, 0.285, 0.0), Vector3(0.0, 0.3, 0.0), Vector3(0.0, 0.318, 0.0), Vector3(0.0, 0.33, 0.0)],
			[0.06, 0.07, 0.066, 0.05], 16, grey, 1.0, 2.0, 0.0, true)
	mb.add_tube([Vector3(0.0, 0.33, 0.0), Vector3(0.0, 0.344, 0.0)], [0.046, 0.046], 16, Color(0.25, 0.25, 0.25), 1.0, 2.0, 0.0, true)
	mb.add_tube([Vector3(0.0, 0.344, 0.0), Vector3(0.0, 0.36, 0.0), Vector3(0.0, 0.378, 0.0), Vector3(0.0, 0.388, 0.0)],
			[0.05, 0.036, 0.024, 0.0], 12, grey, 1.0, 2.0, 0.0, true)
	# Bail and its pivots on the cap.
	var bail: Array[Vector3] = []
	var bail_r: Array[float] = []
	for i in 13:
		var a := float(i) / 12.0 * PI
		bail.append(Vector3(-cos(a) * 0.066, 0.335 + sin(a) * (HURRICANE_HEIGHT - 0.335), 0.0))
		bail_r.append(0.0048)
	mb.add_tube(bail, bail_r, 5, grey, 1.0, 8.0, 0.0, true)
	for side: float in [-1.0, 1.0]:
		mb.add_tube([Vector3(side * 0.058, 0.335, 0.0), Vector3(side * 0.072, 0.335, 0.0)], [0.006, 0.006], 5, grey, 1.0, 4.0, 0.0, true)
	return mb.commit(null, true)


## The glass globe of a hurricane lantern.
static func hurricane_glass() -> ArrayMesh:
	var mb := MeshBuilder.new()
	mb.add_tube([Vector3(0.0, 0.12, 0.0), Vector3(0.0, 0.155, 0.0), Vector3(0.0, 0.205, 0.0), Vector3(0.0, 0.255, 0.0), Vector3(0.0, 0.29, 0.0)],
			[0.044, 0.058, 0.062, 0.054, 0.04], 16, Color.WHITE, 1.0, 2.0, 0.0, false)
	return mb.commit()


## A canvas ground sheet lying on level ground: a subdivided plane with low
## wrinkles, edges that curl up slightly the way laid cloth does, and a thin
## skirt so the edge reads as fabric rather than a slab.
static func cloth_sheet_mesh(size: Vector2, seed_value: int, thickness := 0.006) -> ArrayMesh:
	var mb := MeshBuilder.new()
	var noise := FastNoiseLite.new()
	noise.seed = seed_value
	noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise.frequency = 1.0
	noise.fractal_octaves = 3
	var nx := 14
	var nz := 18
	var rows: Array[PackedInt32Array] = []
	for j in range(nz + 1):
		var v := float(j) / float(nz)
		var row := PackedInt32Array()
		for i in range(nx + 1):
			var u := float(i) / float(nx)
			var x := (u - 0.5) * size.x
			var z := (v - 0.5) * size.y
			var edge := minf(minf(u, 1.0 - u), minf(v, 1.0 - v))
			var curl := (1.0 - smoothstep(0.0, 0.06, edge)) * 0.014
			var wrinkle := noise.get_noise_2d(x * 2.6, z * 2.6) * 0.011 * smoothstep(0.0, 0.12, edge)
			var y := thickness + maxf(wrinkle, -thickness * 0.7) + curl
			row.append(mb.add_vertex(Vector3(x, y, z), Vector3.UP, Vector2(x, z) * 0.9, Color.WHITE, Vector4(1.0, 0.0, 0.0, 1.0)))
		rows.append(row)
	for j in nz:
		for i in nx:
			mb.add_quad_facing(rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i], Vector3.UP)
	mb.recompute_normals()
	# Skirt: the cloth edge down to the ground, facing outward.
	var edges: Array = [
		[rows[0], Vector3.FORWARD], [rows[nz], Vector3.BACK],
	]
	var left := PackedInt32Array()
	var right := PackedInt32Array()
	for j in range(nz + 1):
		left.append(rows[j][0])
		right.append(rows[j][nx])
	edges.append([left, Vector3.LEFT])
	edges.append([right, Vector3.RIGHT])
	for e in edges:
		var line: PackedInt32Array = e[0]
		var out: Vector3 = e[1]
		var feet := PackedInt32Array()
		for idx in line:
			var top := mb.vertices[idx]
			feet.append(mb.add_vertex(Vector3(top.x, 0.0, top.z), out, Vector2(top.x + top.z, 0.0) * 0.9, Color.WHITE, Vector4(1.0, 0.0, 0.0, 1.0)))
		for k in range(line.size() - 1):
			mb.add_quad_facing(line[k], line[k + 1], feet[k + 1], feet[k], out)
	return mb.commit(null, true)


## A short hanging link: a rope or wire from `from` down to `to`.
static func add_hang_link(mb: MeshBuilder, from: Vector3, to: Vector3, radius := 0.006) -> void:
	mb.add_tube([from, from.lerp(to, 0.5), to], [radius, radius, radius], 5, Color.WHITE, 1.0, 6.0, 0.0, true)


## A shepherd's-hook post: a pole with an arc that ends in a hanging point at
## `hook_point(height)`.
static func hook_post_mesh(height: float, seed_value: int) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var mb := MeshBuilder.new()
	var top := Vector3(0.0, height, 0.0)
	var pts: Array[Vector3] = [Vector3(0.0, -0.2, 0.0), Vector3(0.0, height * 0.5, 0.0), top]
	var radii: Array[float] = [0.034, 0.03, 0.026]
	var arc_r := 0.2
	var centre := top + Vector3(arc_r, 0.0, 0.0)
	for i in range(1, 10):
		var a := float(i) / 9.0 * PI
		pts.append(centre + Vector3(-cos(a) * arc_r, sin(a) * arc_r, 0.0))
		radii.append(0.024 - float(i) * 0.0008)
	pts.append(centre + Vector3(arc_r, -0.06, 0.0))
	radii.append(0.016)
	add_timber(mb, pts, radii, 7, seed_value % 4, Color(0.9, 0.86, 0.8), 1.4)
	return mb.commit(null, true)


static func hook_point(height: float) -> Vector3:
	return Vector3(0.4, height - 0.05, 0.0)


static func box_mesh(size: Vector3) -> ArrayMesh:
	var mb := MeshBuilder.new()
	mb.add_box(size, Color.WHITE, 0.8)
	return mb.commit()


static func pole_mesh(height: float, radius: float) -> ArrayMesh:
	var mb := MeshBuilder.new()
	mb.add_tube([Vector3.ZERO, Vector3(0.0, height, 0.0)], [radius, radius * 0.85], 6, Color.WHITE, 1.0, 1.6, 0.0, true)
	return mb.commit()


static func rock(seed_value: int, radius := 1.0) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var mb := MeshBuilder.new()
	var f1 := rng.randf_range(2.4, 5.2)
	var f2 := rng.randf_range(3.1, 7.4)
	var f3 := rng.randf_range(5.0, 9.5)
	var a1 := rng.randf_range(0.08, 0.18)
	var a2 := rng.randf_range(0.04, 0.12)
	var a3 := rng.randf_range(0.02, 0.07)
	var flatten := rng.randf_range(0.18, 0.38)
	var phase := Vector3(rng.randf() * TAU, rng.randf() * TAU, rng.randf() * TAU)
	var stretch := Vector3(rng.randf_range(0.82, 1.22), rng.randf_range(0.55, 0.92), rng.randf_range(0.8, 1.2))
	# Enough rings and segments that the silhouette curves instead of showing
	# flat facets in a close shot, plus a fine fourth term for surface grain.
	var f4 := rng.randf_range(11.0, 17.0)
	var a4 := rng.randf_range(0.012, 0.03)
	mb.add_displaced_sphere(22, 34, radius, func(dir: Vector3) -> float:
		var d := dir * stretch
		var n := 1.0
		n += sin(d.x * f1 + phase.x) * cos(d.y * f1 + phase.y) * a1
		n += sin(d.y * f2 + phase.y) * cos(d.z * f2 + phase.z) * a2
		n += sin(d.z * f3 + dir.x * f3) * a3
		n += sin(d.x * f4 + phase.z) * sin(d.y * f4 * 0.8 + phase.x) * cos(d.z * f4 * 1.1) * a4
		n *= 1.0 - maxf(0.0, -dir.y) * flatten
		return maxf(n, 0.35))
	return mb.commit(null, true)


## A cast-iron cooking pot with a lid and a wire bail, bottom at `base`.
static func add_pot(mb: MeshBuilder, base: Vector3) -> void:
	var profile: Array[Vector3] = []
	var radii: Array[float] = []
	for spec: Array in [[0.26, 0.145], [0.22, 0.15], [0.15, 0.155], [0.07, 0.14], [0.0, 0.1]]:
		profile.append(base + Vector3(0.0, spec[0], 0.0))
		radii.append(spec[1])
	mb.add_tube(profile, radii, 12, Color(0.9, 0.9, 0.9), 1.0, 2.0, 0.0, true)
	mb.add_tube([base + Vector3(0.0, 0.26, 0.0), base + Vector3(0.0, 0.285, 0.0), base + Vector3(0.0, 0.3, 0.0)], [0.15, 0.12, 0.03], 12, Color(0.8, 0.8, 0.8), 1.0, 2.0, 0.0, true)
	var bail: Array[Vector3] = []
	var bail_r: Array[float] = []
	for i in 13:
		var a := float(i) / 12.0 * PI
		bail.append(base + Vector3(-cos(a) * 0.15, 0.24 + sin(a) * 0.22, 0.0))
		bail_r.append(0.008)
	mb.add_tube(bail, bail_r, 5, Color.WHITE, 1.0, 12.0, 0.0, true)


## Appends a sawn board to `mb`. `size` is (across, thickness, length) in the
## board's own frame, length along local -Z..+Z; `xform` places it. Grain runs
## along the length: v follows the length and u is confined to one of the four
## plank strips of the wood texture so the texture's own seams never show.
static func add_board(mb: MeshBuilder, xform: Transform3D, size: Vector3, strip: int, v0: float,
		tint := Color.WHITE, metres_per_tile := 1.0) -> void:
	var h := size * 0.5
	var u0 := float(posmod(strip, 4)) * 0.25 + 0.018
	var u_span := 0.25 - 0.036
	var v_len := size.z / metres_per_tile
	var u_thick := u0 + minf(size.y / metres_per_tile, u_span)
	var faces := [
		# +Y top, -Y bottom: u across, v along.
		[Vector3(-h.x, h.y, h.z), Vector3(h.x, h.y, h.z), Vector3(h.x, h.y, -h.z), Vector3(-h.x, h.y, -h.z), Rect2(u0, v0, u_span, v_len)],
		[Vector3(-h.x, -h.y, -h.z), Vector3(h.x, -h.y, -h.z), Vector3(h.x, -h.y, h.z), Vector3(-h.x, -h.y, h.z), Rect2(u0, v0, u_span, v_len)],
		# +X / -X long sides: u through the thickness, v along.
		[Vector3(h.x, -h.y, -h.z), Vector3(h.x, h.y, -h.z), Vector3(h.x, h.y, h.z), Vector3(h.x, -h.y, h.z), Rect2(u0, v0, u_thick - u0, v_len)],
		[Vector3(-h.x, -h.y, h.z), Vector3(-h.x, h.y, h.z), Vector3(-h.x, h.y, -h.z), Vector3(-h.x, -h.y, -h.z), Rect2(u0, v0, u_thick - u0, v_len)],
		# End grain.
		[Vector3(-h.x, -h.y, h.z), Vector3(h.x, -h.y, h.z), Vector3(h.x, h.y, h.z), Vector3(-h.x, h.y, h.z), Rect2(u0, v0, u_span, size.y / metres_per_tile)],
		[Vector3(h.x, -h.y, -h.z), Vector3(-h.x, -h.y, -h.z), Vector3(-h.x, h.y, -h.z), Vector3(h.x, h.y, -h.z), Rect2(u0, v0, u_span, size.y / metres_per_tile)],
	]
	for face: Array in faces:
		mb.add_quad(xform * face[0], xform * face[1], xform * face[2], xform * face[3], tint, Color(0, 0, 0, 0), face[4])


## A round timber (pile, pole, rail) whose bark-free surface uses one plank
## strip of the wood texture. `points` run along the timber.
static func add_timber(mb: MeshBuilder, points: Array[Vector3], radii: Array[float], sides: int,
		strip: int, tint := Color.WHITE, metres_per_tile := 1.0) -> void:
	var first := mb.vertex_count()
	mb.add_tube(points, radii, sides, tint, 1.0, 1.0 / metres_per_tile, 0.0, true)
	mb.remap_uv(first, 0.25 - 0.036, float(posmod(strip, 4)) * 0.25 + 0.018)


## Catenary-like rope between two points with `sag` metres of droop.
static func add_rope(mb: MeshBuilder, from: Vector3, to: Vector3, sag: float, radius := 0.012, segments := 12) -> void:
	var points: Array[Vector3] = []
	var radii: Array[float] = []
	for i in range(segments + 1):
		var t := float(i) / float(segments)
		points.append(from.lerp(to, t) - Vector3.UP * (sag * 4.0 * t * (1.0 - t)))
		radii.append(radius)
	mb.add_tube(points, radii, 5, Color.WHITE, 1.0, 8.0, 0.0, true)


## Rope wrapped `turns` times around a vertical post of `post_radius` at `centre`.
static func add_rope_coil(mb: MeshBuilder, centre: Vector3, post_radius: float, turns: int, pitch: float, radius := 0.012) -> void:
	var points: Array[Vector3] = []
	var radii: Array[float] = []
	var steps := turns * 14
	for i in range(steps + 1):
		var a := float(i) / 14.0 * TAU
		var y := centre.y + float(i) / float(steps) * pitch * float(turns)
		points.append(Vector3(centre.x + cos(a) * (post_radius + radius * 0.9), y, centre.z + sin(a) * (post_radius + radius * 0.9)))
		radii.append(radius)
	mb.add_tube(points, radii, 5, Color.WHITE, 1.0, 8.0, 0.0, true)


static func sphere_mesh(radius: float, rings := 8, segments := 10) -> SphereMesh:
	var mesh := SphereMesh.new()
	mesh.radius = radius
	mesh.height = radius * 2.0
	mesh.radial_segments = segments
	mesh.rings = rings
	mesh.is_hemisphere = false
	return mesh
