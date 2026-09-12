class_name MeshBuilder
extends RefCounted

## Accumulates triangle geometry in packed arrays and commits it as an
## ArrayMesh surface. Much faster than SurfaceTool for large procedural meshes
## and supports the CUSTOM0 attribute (used for foliage pivots and wind data).

var vertices := PackedVector3Array()
var normals := PackedVector3Array()
var tangents := PackedFloat32Array()
var uvs := PackedVector2Array()
var uv2s := PackedVector2Array()
var colors := PackedColorArray()
var custom0 := PackedFloat32Array()
var indices := PackedInt32Array()

var use_tangents := true
var use_uv2 := false
var use_colors := true
var use_custom0 := false


func vertex_count() -> int:
	return vertices.size()


func triangle_count() -> int:
	return indices.size() / 3


## Adds one vertex and returns its index.
func add_vertex(position: Vector3, normal: Vector3, uv: Vector2, color := Color.WHITE,
		tangent := Vector4(1.0, 0.0, 0.0, 1.0), custom := Color(0, 0, 0, 0), uv2 := Vector2.ZERO) -> int:
	vertices.push_back(position)
	normals.push_back(normal)
	uvs.push_back(uv)
	if use_colors:
		colors.push_back(color)
	if use_tangents:
		tangents.push_back(tangent.x)
		tangents.push_back(tangent.y)
		tangents.push_back(tangent.z)
		tangents.push_back(tangent.w)
	if use_custom0:
		custom0.push_back(custom.r)
		custom0.push_back(custom.g)
		custom0.push_back(custom.b)
		custom0.push_back(custom.a)
	if use_uv2:
		uv2s.push_back(uv2)
	return vertices.size() - 1


func add_triangle(a: int, b: int, c: int) -> void:
	indices.push_back(a)
	indices.push_back(b)
	indices.push_back(c)


## Corners are given counter-clockwise as seen from the front; Godot treats
## clockwise triangles as front-facing, so the emitted order is reversed.
func add_quad_indices(a: int, b: int, c: int, d: int) -> void:
	add_triangle(a, c, b)
	add_triangle(a, d, c)


## Quad a-b-c-d (a ring order, either winding) whose front should face
## `outward`; the winding is chosen accordingly.
func add_quad_facing(a: int, b: int, c: int, d: int, outward: Vector3) -> void:
	var geometric := (vertices[b] - vertices[a]).cross(vertices[c] - vertices[a])
	if geometric.dot(outward) > 0.0:
		add_quad_indices(a, b, c, d)
	else:
		add_quad_indices(a, d, c, b)


## Adds a planar quad (p0..p3 counter-clockwise as seen from the front) with a
## flat normal and tangent along the p0->p1 edge. UVs: p0=(0,1) p1=(1,1)
## p2=(1,0) p3=(0,0) so the image top maps to the p2/p3 edge.
func add_quad(p0: Vector3, p1: Vector3, p2: Vector3, p3: Vector3, color := Color.WHITE,
		custom := Color(0, 0, 0, 0), uv_rect := Rect2(0.0, 0.0, 1.0, 1.0)) -> void:
	var n := (p1 - p0).cross(p3 - p0).normalized()
	var t := (p1 - p0).normalized()
	var tangent := Vector4(t.x, t.y, t.z, 1.0)
	var u0 := uv_rect.position.x
	var v0 := uv_rect.position.y
	var u1 := uv_rect.end.x
	var v1 := uv_rect.end.y
	var a := add_vertex(p0, n, Vector2(u0, v1), color, tangent, custom)
	var b := add_vertex(p1, n, Vector2(u1, v1), color, tangent, custom)
	var c := add_vertex(p2, n, Vector2(u1, v0), color, tangent, custom)
	var d := add_vertex(p3, n, Vector2(u0, v0), color, tangent, custom)
	add_quad_indices(a, b, c, d)


## Axis-aligned box centred on the origin. Faces are authored from the outside
## so the existing clockwise winding stays consistent.
func add_box(size: Vector3, color := Color.WHITE, uv_scale := 1.0) -> void:
	var h := size * 0.5
	var ux := size.x * uv_scale
	var uy := size.y * uv_scale
	var uz := size.z * uv_scale
	# +Y
	add_quad(Vector3(-h.x, h.y, -h.z), Vector3(h.x, h.y, -h.z), Vector3(h.x, h.y, h.z), Vector3(-h.x, h.y, h.z),
			color, Color(0, 0, 0, 0), Rect2(0, 0, ux, uz))
	# -Y
	add_quad(Vector3(-h.x, -h.y, h.z), Vector3(h.x, -h.y, h.z), Vector3(h.x, -h.y, -h.z), Vector3(-h.x, -h.y, -h.z),
			color, Color(0, 0, 0, 0), Rect2(0, 0, ux, uz))
	# +X
	add_quad(Vector3(h.x, -h.y, -h.z), Vector3(h.x, -h.y, h.z), Vector3(h.x, h.y, h.z), Vector3(h.x, h.y, -h.z),
			color, Color(0, 0, 0, 0), Rect2(0, 0, uz, uy))
	# -X
	add_quad(Vector3(-h.x, -h.y, h.z), Vector3(-h.x, -h.y, -h.z), Vector3(-h.x, h.y, -h.z), Vector3(-h.x, h.y, h.z),
			color, Color(0, 0, 0, 0), Rect2(0, 0, uz, uy))
	# +Z
	add_quad(Vector3(-h.x, -h.y, h.z), Vector3(h.x, -h.y, h.z), Vector3(h.x, h.y, h.z), Vector3(-h.x, h.y, h.z),
			color, Color(0, 0, 0, 0), Rect2(0, 0, ux, uy))
	# -Z
	add_quad(Vector3(h.x, -h.y, -h.z), Vector3(-h.x, -h.y, -h.z), Vector3(-h.x, h.y, -h.z), Vector3(h.x, h.y, -h.z),
			color, Color(0, 0, 0, 0), Rect2(0, 0, ux, uy))


## Sweeps a ring of `sides` vertices along a polyline with per-point radii,
## using parallel-transport frames so the texture never twists. `v_scale`
## controls texture repeats along the length (in metres per repeat, inverted).
## Returns the frame (side, up vectors) at the last point for continuing tubes.
func add_tube(points: Array[Vector3], radii: Array[float], sides: int, color := Color.WHITE,
		u_repeats := 1.0, v_per_metre := 1.0, start_v := 0.0, cap_end := false,
		ring_color_fn: Callable = Callable()) -> Dictionary:
	assert(points.size() >= 2 and radii.size() == points.size())
	var n := points.size()
	var tangents_along: Array[Vector3] = []
	for i in n:
		var t: Vector3
		if i == 0:
			t = points[1] - points[0]
		elif i == n - 1:
			t = points[i] - points[i - 1]
		else:
			t = points[i + 1] - points[i - 1]
		tangents_along.append(t.normalized() if t.length_squared() > 1e-10 else Vector3.UP)

	# Initial right-handed frame (side, up, tangent) perpendicular to the first
	# tangent; right-handedness makes the ring winding come out clockwise.
	var t0 := tangents_along[0]
	var side := t0.cross(Vector3.UP)
	if side.length_squared() < 1e-6:
		side = t0.cross(Vector3.RIGHT)
	side = side.normalized()
	var up := t0.cross(side).normalized()

	var ring_start: Array[int] = []
	var v := start_v
	for i in n:
		if i > 0:
			# Parallel transport: rotate the previous frame by the rotation that
			# takes the previous tangent to the current one.
			var prev_t := tangents_along[i - 1]
			var cur_t := tangents_along[i]
			var axis := prev_t.cross(cur_t)
			var s := axis.length()
			if s > 1e-6:
				var angle := asin(clampf(s, -1.0, 1.0))
				if prev_t.dot(cur_t) < 0.0:
					angle = PI - angle
				side = side.rotated(axis / s, angle)
				up = up.rotated(axis / s, angle)
			v += points[i].distance_to(points[i - 1]) * v_per_metre
		ring_start.append(vertices.size())
		var r := radii[i]
		var ring_color := color
		if ring_color_fn.is_valid():
			ring_color = ring_color_fn.call(i)
		for sIdx in range(sides + 1):
			var a := float(sIdx) / float(sides) * TAU
			var normal := (side * cos(a) + up * sin(a)).normalized()
			var tangent_vec := (-side * sin(a) + up * cos(a)).normalized()
			# cross(normal, tangent) points along +v here; Godot's binormal must
			# point towards the image top (-v), hence the negative sign.
			add_vertex(points[i] + normal * r, normal,
					Vector2(float(sIdx) / float(sides) * u_repeats, v), ring_color,
					Vector4(tangent_vec.x, tangent_vec.y, tangent_vec.z, -1.0))

	for i in range(n - 1):
		var r0 := ring_start[i]
		var r1 := ring_start[i + 1]
		for sIdx in sides:
			var a := r0 + sIdx
			var b := r0 + sIdx + 1
			var c := r1 + sIdx + 1
			var d := r1 + sIdx
			add_triangle(a, c, b)
			add_triangle(a, d, c)

	if cap_end:
		var centre := points[n - 1]
		var cn := tangents_along[n - 1]
		var ci := add_vertex(centre, cn, Vector2(0.5, 0.5), color, Vector4(side.x, side.y, side.z, 1.0))
		var last_ring := ring_start[n - 1]
		for sIdx in sides:
			var a := last_ring + sIdx
			var b := last_ring + sIdx + 1
			add_triangle(ci, b, a)
	return {side = side, up = up, v = v}


## Adds a UV sphere deformed by `displace(dir: Vector3) -> float` (radius
## multiplier), with smooth normals recomputed from the displaced surface.
func add_displaced_sphere(rings: int, segments: int, radius: float, displace: Callable,
		color := Color.WHITE, uv_scale := 1.0) -> void:
	var base := vertices.size()
	var positions: Array[Vector3] = []
	for r in range(rings + 1):
		var phi := PI * float(r) / float(rings)
		for s in range(segments + 1):
			var theta := TAU * float(s) / float(segments)
			var dir := Vector3(sin(phi) * cos(theta), cos(phi), sin(phi) * sin(theta))
			var p := dir * radius * float(displace.call(dir))
			positions.append(p)
	for r in range(rings + 1):
		for s in range(segments + 1):
			var idx := r * (segments + 1) + s
			var p := positions[idx]
			var n := _sphere_grid_normal(positions, rings, segments, r, s)
			var uv := Vector2(float(s) / float(segments), float(r) / float(rings)) * uv_scale
			var tangent := n.cross(Vector3.UP)
			if tangent.length_squared() < 1e-6:
				tangent = Vector3.RIGHT
			tangent = tangent.normalized()
			add_vertex(p, n, uv, color, Vector4(tangent.x, tangent.y, tangent.z, -1.0))
	for r in rings:
		for s in segments:
			var a := base + r * (segments + 1) + s
			var b := a + 1
			var c := a + segments + 1
			var d := c + 1
			# Clockwise from outside: a -> c -> d -> b. Skip the degenerate
			# triangle at each pole.
			if r < rings - 1:
				add_triangle(a, c, d)
			if r > 0:
				add_triangle(a, d, b)


func _sphere_grid_normal(positions: Array[Vector3], rings: int, segments: int, r: int, s: int) -> Vector3:
	var idx := func(rr: int, ss: int) -> Vector3:
		rr = clampi(rr, 0, rings)
		ss = posmod(ss, segments)
		return positions[rr * (segments + 1) + ss]
	var p: Vector3 = idx.call(r, s)
	var right: Vector3 = idx.call(r, s + 1)
	var left: Vector3 = idx.call(r, s - 1)
	var down: Vector3 = idx.call(r + 1, s)
	var up: Vector3 = idx.call(r - 1, s)
	if r == 0 or r == rings:
		return p.normalized()
	var du := right - left
	var dv := down - up
	var n := dv.cross(du)
	if n.length_squared() < 1e-12 or n.dot(p) < 0.0:
		n = -n if n.dot(p) < 0.0 else p
	return n.normalized()


## Recomputes smooth per-vertex normals from the triangle list (area weighted).
func recompute_normals() -> void:
	var acc := PackedVector3Array()
	acc.resize(vertices.size())
	acc.fill(Vector3.ZERO)
	for i in range(0, indices.size(), 3):
		var a := indices[i]
		var b := indices[i + 1]
		var c := indices[i + 2]
		var n := (vertices[b] - vertices[a]).cross(vertices[c] - vertices[a])
		acc[a] += n
		acc[b] += n
		acc[c] += n
	for i in acc.size():
		var n := acc[i]
		# Retain the authored outward direction. Clockwise front faces have
		# the opposite cross-product sign from the usual mathematical normal.
		if n.dot(normals[i]) < 0.0:
			n = -n
		normals[i] = n.normalized() if n.length_squared() > 1e-12 else normals[i]


func is_empty() -> bool:
	return vertices.is_empty()


## UV-derived tangent frames, including mirrored fabric panels. Call after
## smoothing normals so normal maps follow the finished surface.
func recompute_tangents() -> void:
	var us := PackedVector3Array()
	var vs := PackedVector3Array()
	us.resize(vertices.size())
	vs.resize(vertices.size())
	for i in range(0, indices.size(), 3):
		var a := indices[i]
		var b := indices[i + 1]
		var c := indices[i + 2]
		var e1 := vertices[b] - vertices[a]
		var e2 := vertices[c] - vertices[a]
		var uv1 := uvs[b] - uvs[a]
		var uv2 := uvs[c] - uvs[a]
		var determinant := uv1.x * uv2.y - uv1.y * uv2.x
		if absf(determinant) < 1e-10:
			continue
		var u := (e1 * uv2.y - e2 * uv1.y) / determinant
		var v := (e2 * uv1.x - e1 * uv2.x) / determinant
		for index: int in [a, b, c]:
			us[index] += u
			vs[index] += v
	tangents.resize(vertices.size() * 4)
	for i in vertices.size():
		var t := tangent_for(normals[i], us[i], vs[i])
		for axis in 4:
			tangents[i * 4 + axis] = t[axis]


## Rescales the UVs of every vertex added since `from_index`, e.g. to confine a
## tube's u range to one plank strip of the wood texture.
func remap_uv(from_index: int, u_scale: float, u_offset: float, v_scale := 1.0, v_offset := 0.0) -> void:
	for i in range(from_index, uvs.size()):
		var uv := uvs[i]
		uvs[i] = Vector2(uv.x * u_scale + u_offset, uv.y * v_scale + v_offset)


## Tints every vertex added since `from_index`.
func tint_from(from_index: int, color: Color) -> void:
	if not use_colors:
		return
	for i in range(from_index, colors.size()):
		colors[i] = color


## Tangent for a surface point given its normal, the direction in which the
## texture's u grows and the direction in which v grows. The sign is chosen so
## that Godot's binormal points towards the image top (decreasing v).
static func tangent_for(normal: Vector3, u_dir: Vector3, v_dir: Vector3) -> Vector4:
	var t := (u_dir - normal * normal.dot(u_dir))
	if t.length_squared() < 1e-10:
		t = normal.cross(Vector3.UP)
		if t.length_squared() < 1e-10:
			t = Vector3.RIGHT
	t = t.normalized()
	var w := 1.0 if normal.cross(t).dot(-v_dir) >= 0.0 else -1.0
	return Vector4(t.x, t.y, t.z, w)


func _arrays() -> Array:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	if use_tangents:
		arrays[Mesh.ARRAY_TANGENT] = tangents
	if use_colors:
		arrays[Mesh.ARRAY_COLOR] = colors
	if use_custom0:
		arrays[Mesh.ARRAY_CUSTOM0] = custom0
	if use_uv2:
		arrays[Mesh.ARRAY_TEX_UV2] = uv2s
	return arrays


func _format_flags() -> int:
	var flags := 0
	if use_custom0:
		flags |= Mesh.ARRAY_CUSTOM_RGBA_FLOAT << Mesh.ARRAY_FORMAT_CUSTOM0_SHIFT
	return flags


## Commits the geometry as a single surface. With `generate_lods` the mesh is
## routed through ImporterMesh so the renderer gets automatic discrete LODs.
func commit(material: Material = null, generate_lods := false, existing: ArrayMesh = null) -> ArrayMesh:
	if generate_lods:
		var importer := ImporterMesh.new()
		if existing != null:
			for s in existing.get_surface_count():
				importer.add_surface(Mesh.PRIMITIVE_TRIANGLES, existing.surface_get_arrays(s), [], {},
						existing.surface_get_material(s), existing.surface_get_name(s), existing.surface_get_format(s))
		importer.add_surface(Mesh.PRIMITIVE_TRIANGLES, _arrays(), [], {}, material, "", _format_flags())
		importer.generate_lods(25.0, 60.0, [])
		return importer.get_mesh()
	var mesh := existing if existing != null else ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, _arrays(), [], {}, _format_flags())
	if material != null:
		mesh.surface_set_material(mesh.get_surface_count() - 1, material)
	return mesh
