class_name MeadowPlants
extends RefCounted

## Temperate summer meadow plants, modelled at their real scale. Small curved
## petals hold up in a close dolly without the crossed-card flower illusion.
enum Kind { YARROW, BUTTERCUP, CLOVER, DAISY }

static func flower(kind: int, seed_value: int) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var mb := MeshBuilder.new()
	var green := Color(0.06, 0.18, 0.045)
	var h := [0.64, 0.50, 0.36, 0.61][kind] as float
	for stalk in (2 if kind == Kind.CLOVER else 3):
		var a := float(stalk) * 2.39996 + rng.randf()
		var out := Vector3(cos(a), 0, sin(a))
		var head := out * rng.randf_range(0.035, 0.12) + Vector3.UP * h * rng.randf_range(0.70, 1.0)
		mb.add_tube([Vector3.ZERO, head * 0.55 - out * 0.02, head], [0.0025, 0.0018, 0.001], 4, green)
		for k in 3:
			var origin := head * (0.20 + float(k) * 0.16)
			var dir := out.rotated(Vector3.UP, float(k) * 2.39)
			if kind == Kind.YARROW:
				for leaflet in 8:
					var t := float(leaflet) / 8.0
					var at := origin + dir * t * 0.085 + Vector3.UP * t * 0.018
					for sign_value: float in [-1.0, 1.0]:
						_petal(mb, at, dir.rotated(Vector3.UP, sign_value * 0.95), 0.022 * (1.0 - t * 0.65), 0.003, 0.003, green)
			elif kind == Kind.CLOVER:
				for leaflet in 3:
					_petal(mb, origin + dir * 0.025, dir.rotated(Vector3.UP, (leaflet - 1) * 1.05), 0.028, 0.026, 0.004, green)
			else:
				_petal(mb, origin, dir, 0.065, 0.016, 0.017, green)
		var bloom_start := mb.vertex_count()
		match kind:
			Kind.YARROW:
				for lobe in 7:
					var centre := head + Vector3(cos(float(lobe) * 2.4), rng.randf_range(-0.08, 0.16), sin(float(lobe) * 2.4)) * rng.randf_range(0.019, 0.029)
					mb.add_tube([head - Vector3.UP * 0.035, centre], [0.001, 0.0005], 3, green)
					for floret in 6:
						var p := centre + Vector3(cos(float(floret) * 2.4), 0, sin(float(floret) * 2.4)) * 0.007
						_yarrow_floret(mb, p)
			Kind.CLOVER:
				_flower_core(mb, head + Vector3.UP * 0.014, 0.015, 1.0, Color(0.38, 0.09, 0.19))
				for floret in 64:
					var a2 := float(floret) * 2.39996
					var y := float(floret) / 64.0
					var r := sqrt(1.0 - pow(y * 2.0 - 1.0, 2.0)) * 0.018
					var p := head + Vector3(cos(a2) * r, y * 0.028, sin(a2) * r)
					_petal(mb, p, Vector3(cos(a2), 0.6, sin(a2)).normalized(), 0.009, 0.003, 0.004, Color(0.48, 0.14, 0.28).lerp(Color(0.72, 0.38, 0.47), y))
			_:
				var count := 5 if kind == Kind.BUTTERCUP else 13
				var color := Color(0.84, 0.54, 0.035) if kind == Kind.BUTTERCUP else Color(0.83, 0.81, 0.68)
				for petal in count:
					var dir := Vector3(cos(float(petal) / count * TAU), 0, sin(float(petal) / count * TAU))
					_petal(mb, head, dir, (0.021 if count == 5 else 0.03) * rng.randf_range(0.88, 1.12), 0.016 if count == 5 else 0.006, rng.randf_range(0.012, 0.018) if count == 5 else rng.randf_range(-0.002, 0.006), color)
				_flower_core(mb, head + Vector3.UP * 0.005, 0.0075, 0.80 if count == 5 else 0.48, Color(0.72, 0.44, 0.055))
		# Stems do not all hold their blooms exactly level. Preserve yarrow's
		# corymb, with stronger nods and differing cups in individual daisies.
		var tilt := Basis(out.rotated(Vector3.UP, PI * 0.5), rng.randf_range(0.04, 0.16) if kind == Kind.YARROW else rng.randf_range(0.08, 0.40))
		for i in range(bloom_start, mb.vertex_count()):
			mb.vertices[i] = head + tilt * (mb.vertices[i] - head)
			mb.normals[i] = tilt * mb.normals[i]

	mb.recompute_normals()
	return mb.commit(null, true)


## Millimetre-scale yarrow florets need a scalloped, gently convex surface,
## not hundreds of strip triangles per bloom. This keeps the meadow cheap.
static func _yarrow_floret(mb: MeshBuilder, p: Vector3) -> void:
	var center := mb.add_vertex(p + Vector3.UP * 0.001, Vector3.UP, Vector2.ONE * 0.5, Color(0.70, 0.66, 0.48))
	for i in 16:
		var a := float(i) / 15.0 * TAU
		var r := 0.0047 * (0.86 + 0.14 * cos(a * 5.0))
		# Off-white with a green cast: pure white florets bloomed into paper discs.
		mb.add_vertex(p + Vector3(cos(a) * r, 0, sin(a) * r), Vector3.UP, Vector2(cos(a), sin(a)) * 0.5 + Vector2.ONE * 0.5, Color(0.72, 0.72, 0.58))
		if i > 0:
			mb.add_triangle(center, center + i, center + i + 1)


static func _flower_core(mb: MeshBuilder, at: Vector3, radius: float, vertical: float, color: Color) -> void:
	var start := mb.vertex_count()
	mb.add_displaced_sphere(8, 20, radius, func(_dir: Vector3) -> float: return 1.0, color)
	for i in range(start, mb.vertex_count()):
		mb.vertices[i].y *= vertical
		mb.vertices[i] += at
		mb.normals[i] = (mb.normals[i] / Vector3(1, vertical, 1)).normalized()


static func _petal(mb: MeshBuilder, p: Vector3, out: Vector3, length: float, width: float, lift: float, color: Color) -> void:
	var side := out.cross(Vector3.UP).normalized()
	if side.length_squared() < 0.01:
		side = Vector3.RIGHT
	var prev := PackedInt32Array()
	for row in 7:
		var t := float(row) / 6.0
		var centre := p + out * t * length + Vector3.UP * lift * sin(t * PI * 0.8)
		var w := width * (0.015 + pow(maxf(sin(t * PI), 0.0), 0.7) * 0.985) * 0.5
		var curl := Vector3.UP * sin(t * PI) * width * 0.12
		var left := mb.add_vertex(centre - side * w + curl, Vector3.UP, Vector2(0, t), color)
		var mid := mb.add_vertex(centre, Vector3.UP, Vector2(0.5, t), color)
		var right := mb.add_vertex(centre + side * w + curl, Vector3.UP, Vector2(1, t), color)
		if row > 0:
			mb.add_quad_indices(prev[0], left, mid, prev[1])
			mb.add_quad_indices(prev[1], mid, right, prev[2])
		prev = PackedInt32Array([left, mid, right])


## Loose, whorled panicles and compact tapered heads sit above the basal
## grass leaves. Fine terminal spikelets leave air between the branches;
## enlarged flower petals would turn the panicle into a solid ornament.
static func seed_heads(kind := 0, seed_value := 177) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var mb := MeshBuilder.new()
	var stalk_color := Color(0.24, 0.29, 0.11)
	var seed_color := Color(0.52, 0.49, 0.30)
	for stem in 3:
		var a := float(stem) * 2.39996 + rng.randf_range(-0.18, 0.18)
		var out := Vector3(cos(a), 0, sin(a))
		var h := rng.randf_range(0.69, 0.96)
		var head := out * rng.randf_range(0.07, 0.14) + Vector3.UP * h
		mb.add_tube([Vector3.ZERO, head * 0.42 - out * 0.03, head], [0.0017, 0.0012, 0.00065], 4, stalk_color)
		for leaf in 2:
			var origin := head * (0.28 + float(leaf) * 0.21)
			_petal(mb, origin, out.rotated(Vector3.UP, float(leaf) * 2.39996), 0.13, 0.0045, 0.031, stalk_color)
		if kind == 0:
			var crown := head + Vector3.UP * 0.22 + out * 0.015
			mb.add_tube([head, crown], [0.00065, 0.00025], 3, stalk_color)
			for tier in 4:
				var t := float(tier) / 4.0
				var at := head.lerp(crown, t)
				for branch in 3:
					var angle := a + float(branch) * TAU / 3.0 + float(tier) * 0.77
					var dir := Vector3(cos(angle), rng.randf_range(0.35, 0.75), sin(angle)).normalized()
					var tip := at + dir * (0.13 - t * 0.10) * rng.randf_range(0.88, 1.12)
					mb.add_tube([at, tip], [0.00050, 0.00024], 3, stalk_color)
					for spike in 2:
						var p := at.lerp(tip, 0.70 + float(spike) * 0.30)
						_seed_spikelet(mb, p, (dir + Vector3.UP * 0.4).normalized(), 0.012, 0.0032, seed_color)
			_seed_spikelet(mb, crown, Vector3.UP, 0.011, 0.0026, seed_color)
		else:
			var crown := head + Vector3.UP * 0.13 + out * 0.012
			mb.add_tube([head, crown], [0.00065, 0.00024], 3, stalk_color)
			for tier in 7:
				var t := float(tier) / 7.0
				for spike in 3:
					var angle := a + float(spike) * TAU / 3.0 + float(tier) * 2.39996
					var dir := Vector3(cos(angle) * 0.36, 1.0, sin(angle) * 0.36).normalized()
					_seed_spikelet(mb, head.lerp(crown, t), dir, 0.023 * (1.0 - t * 0.48), 0.0045 * (1.0 - t * 0.55), seed_color)
	return mb.commit(null, true)


## Two tiny folded blades keep a seed visible from oblique views without
## alpha cards or the vertex cost of a full seven-row flower petal.
static func _seed_spikelet(mb: MeshBuilder, at: Vector3, direction: Vector3,
		length: float, width: float, color: Color) -> void:
	var side := direction.cross(Vector3.RIGHT).normalized()
	if side.length_squared() < 0.01:
		side = Vector3.FORWARD
	for fold in 2:
		var across := side.rotated(direction, float(fold) * PI * 0.5)
		var mid := at + direction * length * 0.42
		mb.add_quad(at, mid + across * width * 0.5, at + direction * length,
			mid - across * width * 0.5, color)
