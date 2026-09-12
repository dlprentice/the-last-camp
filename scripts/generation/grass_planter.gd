class_name GrassPlanter
extends RefCounted

## Plans individual grass blades into square chunks so each chunk can be
## frustum-culled independently. Blade placement follows the terrain's grass
## suitability (open, gentle, dry ground) and clumps around tuft centres.
## Output is a MultiMesh buffer per chunk, ready to upload.

class Chunk:
	var origin: Vector2
	var count := 0
	var buffer := PackedFloat32Array()
	var aabb := AABB()

const CHUNK_SIZE := 8.0
## Stand density multiplier over the authored clump count: the meadow is
## meant to read as thick grass everywhere the camera can see.
const NEAR_DENSITY_BOOST := 3.2
const FLOATS_PER_INSTANCE := 20
const SUITABILITY_RESOLUTION := 1.0
const CLUMPS_PER_SQUARE_METRE := 22.0
## GDScript does not scale past a few threads (a shared VM lock), and hogging
## the pool starves the renderer's own tasks; three low-priority tasks is the
## sweet spot measured on a 24-thread machine.
const PLANNING_TASKS := 3
## Small blade groups replace subpixel near clumps with a broad overlap.
## Larger spatial batches keep the distant hills cheap to cull and draw.
const DISTANT_CHUNK_SIZE := 32.0
const DISTANT_EXTENT := 690.0

var field: TerrainField
var extent: float
var density: float
var seed_value := 424242
var suitability: ScalarField
var chunks: Array[Chunk] = []
var total_blades := 0
var distant_chunks: Array[Chunk] = []
var distant_clumps := 0
var patches := meadow_patch_noise()


func _init(p_field: TerrainField, p_extent: float, p_density: float, p_seed := 424242) -> void:
	field = p_field
	extent = p_extent
	density = p_density
	seed_value = p_seed


## Bakes suitability once at 1 m so per-blade queries stay cheap.
func bake_suitability() -> void:
	var res := int(extent * 2.0 / SUITABILITY_RESOLUTION) + 1
	suitability = ScalarField.new(res, extent * 2.0, 0.0)
	suitability.fill_with(func(x: float, z: float) -> float:
		if Vector2(x, z).length() > extent:
			return 0.0
		return field.grass_suitability(x, z, field.height_fast(x, z)))


## Plans every chunk. Chunks are independent, so they are distributed over the
## worker thread pool; each uses its own RNG seeded from the chunk index so the
## result is deterministic regardless of thread scheduling.
func plan() -> void:
	if suitability == null:
		bake_suitability()
	chunks.clear()
	total_blades = 0
	distant_chunks.clear()
	distant_clumps = 0
	var origins: Array[Vector2] = []
	var half_chunks := int(ceil(extent / CHUNK_SIZE))
	for cz in range(-half_chunks, half_chunks):
		for cx in range(-half_chunks, half_chunks):
			var origin := Vector2(float(cx) * CHUNK_SIZE, float(cz) * CHUNK_SIZE)
			var centre := origin + Vector2.ONE * (CHUNK_SIZE * 0.5)
			if centre.length() > extent + CHUNK_SIZE:
				continue
			origins.append(origin)
	var results: Array = []
	results.resize(origins.size())
	var base_seed := seed_value
	var task := func(index: int) -> void:
		var local_rng := RandomNumberGenerator.new()
		# Stable world-cell seeds retain the same plants when quality changes
		# the extent; indexing the origins array moved every clump instead.
		var cell := origins[index] / CHUNK_SIZE
		local_rng.seed = base_seed + int(cell.x) * 73856093 + int(cell.y) * 19349663
		results[index] = _plan_chunk(origins[index], local_rng)
	var group := WorkerThreadPool.add_group_task(task, origins.size(), PLANNING_TASKS, false, "grass")
	WorkerThreadPool.wait_for_group_task_completion(group)
	for r in results:
		var chunk: Chunk = r
		if chunk != null and chunk.count > 0:
			chunks.append(chunk)
			total_blades += chunk.count
	if extent >= 200.0:
		_plan_distant()


## Average suitability over the chunk decides how many candidates to try.
func _chunk_coverage(origin: Vector2) -> float:
	var sum := 0.0
	for iz in 4:
		for ix in 4:
			var p := origin + Vector2((float(ix) + 0.5) * CHUNK_SIZE / 4.0, (float(iz) + 0.5) * CHUNK_SIZE / 4.0)
			sum += suitability.sample(p.x, p.y)
	return sum / 16.0


func _plan_chunk(origin: Vector2, rng: RandomNumberGenerator) -> Chunk:
	var chunk := Chunk.new()
	chunk.origin = origin
	var coverage := _chunk_coverage(origin)
	if coverage < 0.02:
		return chunk
	# Candidates are not scaled by the chunk's average cover: per-blade
	# rejection alone shapes the density, so chunk borders never show.
	var area := CHUNK_SIZE * CHUNK_SIZE
	var range_from_camp := (origin + Vector2.ONE * CHUNK_SIZE * 0.5).length()
	var distance_scale := lerpf(1.0, 0.12, smoothstep(32.0, 160.0, range_from_camp))
	distance_scale *= lerpf(1.0, 0.10, smoothstep(140.0, 300.0, range_from_camp))
	var footprint := 1.0 / sqrt(distance_scale)
	var candidates := int(area * density * CLUMPS_PER_SQUARE_METRE * NEAR_DENSITY_BOOST * distance_scale)
	var floats := PackedFloat32Array()
	floats.resize(candidates * FLOATS_PER_INSTANCE)
	var count := 0
	var min_pos := Vector3(INF, INF, INF)
	var max_pos := Vector3(-INF, -INF, -INF)

	# Tufts: blades cluster around a few dozen centres per chunk.
	var tufts := PackedVector2Array()
	var tuft_count := 26
	for i in tuft_count:
		tufts.append(origin + Vector2(rng.randf() * CHUNK_SIZE, rng.randf() * CHUNK_SIZE))

	for i in candidates:
		var p: Vector2
		if rng.randf() < 0.22:
			var t := tufts[rng.randi() % tuft_count]
			var r := rng.randf_range(0.0, 0.55) * sqrt(rng.randf())
			var a := rng.randf() * TAU
			p = t + Vector2(cos(a), sin(a)) * r
			if p.x < origin.x or p.y < origin.y or p.x >= origin.x + CHUNK_SIZE or p.y >= origin.y + CHUNK_SIZE:
				continue
		else:
			p = origin + Vector2(rng.randf() * CHUNK_SIZE, rng.randf() * CHUNK_SIZE)
		var suit := suitability.sample(p.x, p.y)
		if rng.randf() > suit:
			continue
		# Exact clearance at a narrow walking route cannot be reconstructed from
		# the one-metre suitability grid alone.
		if p.x > -19.0 and p.x < 12.0 and p.y > -8.0 and p.y < 73.0:
			if field.walking_distance(p) < 0.52 or TerrainField.camp_wear(p) > 0.94:
				continue
		var y := field.height_fast(p.x, p.y)
		if maxf(absf(p.x), absf(p.y)) > TerrainBuilder.INNER_UNIFORM_HALF:
			y = field.surface_height(p.x, p.y)
		if y < TerrainField.WATER_LEVEL + 0.05:
			continue
		var dry := field.dryness(p.x, p.y)
		var cover := field.woodland_cover(p.x, p.y)
		var patch := patches.get_noise_2d(p.x, p.y)
		var growth := patches.get_noise_2d(p.x + 73.3, p.y - 41.7)
		var mature := meadow_senescence(dry, cover, patch)
		# A continuous shorter sward supports the larger fountain-shaped tufts.
		# Older leaves tend to settle into the lower sward. A separate regrowth
		# pattern avoids every pale patch also becoming a uniformly tall island.
		var height := field.meadow_height(p.x, p.y) * 0.82 * rng.randf_range(0.45, 1.13)
		height *= lerpf(0.78, 1.06, smoothstep(-0.40, 0.40, growth)) * lerpf(1.0, 0.74, cover)
		height *= lerpf(1.0, 0.80, mature)
		var yaw := rng.randf() * TAU
		var lean_angle := yaw + rng.randf_range(-0.6, 0.6)
		var tint := _blade_tint(mature, cover, rng.randf())
		# Inline the 3x4 transform (rotation about Y, y-scaled) to avoid allocations.
		var c := cos(yaw)
		var s := sin(yaw)
		var o := count * FLOATS_PER_INSTANCE
		floats[o + 0] = c * footprint
		floats[o + 1] = 0.0
		floats[o + 2] = s * footprint
		floats[o + 3] = p.x
		floats[o + 4] = 0.0
		floats[o + 5] = height
		floats[o + 6] = 0.0
		floats[o + 7] = y - 0.02
		floats[o + 8] = -s * footprint
		floats[o + 9] = 0.0
		floats[o + 10] = c * footprint
		floats[o + 11] = p.y
		floats[o + 12] = tint.r
		floats[o + 13] = tint.g
		floats[o + 14] = tint.b
		floats[o + 15] = tint.a
		floats[o + 16] = rng.randf()
		floats[o + 17] = cos(lean_angle) * 0.5 + 0.5
		floats[o + 18] = sin(lean_angle) * 0.5 + 0.5
		floats[o + 19] = rng.randf_range(0.15, 0.6)
		count += 1
		min_pos = Vector3(minf(min_pos.x, p.x), minf(min_pos.y, y), minf(min_pos.z, p.y))
		max_pos = Vector3(maxf(max_pos.x, p.x), maxf(max_pos.y, y + height), maxf(max_pos.z, p.y))

	floats.resize(count * FLOATS_PER_INSTANCE)
	chunk.count = count
	chunk.buffer = floats
	if count > 0:
		var margin := maxf(0.6, footprint * 0.8)
		chunk.aabb = AABB(min_pos - Vector3(margin, 0.2, margin), (max_pos - min_pos) + Vector3(margin * 2.0, 0.6, margin * 2.0))
	return chunk


func _plan_distant() -> void:
	var origins: Array[Vector2] = []
	var half_chunks := ceili(DISTANT_EXTENT / DISTANT_CHUNK_SIZE)
	for z in range(-half_chunks, half_chunks):
		for x in range(-half_chunks, half_chunks):
			var origin := Vector2(x, z) * DISTANT_CHUNK_SIZE
			var radius := (origin + Vector2.ONE * DISTANT_CHUNK_SIZE * 0.5).length()
			if radius > 110.0 and radius < DISTANT_EXTENT + DISTANT_CHUNK_SIZE:
				origins.append(origin)
	var results: Array = []
	results.resize(origins.size())
	var task := func(index: int) -> void:
		var cell := origins[index] / DISTANT_CHUNK_SIZE
		var rng := RandomNumberGenerator.new()
		rng.seed = seed_value + 7301 + int(cell.x) * 73856093 + int(cell.y) * 19349663
		results[index] = _plan_distant_chunk(origins[index], rng)
	var group := WorkerThreadPool.add_group_task(task, origins.size(), PLANNING_TASKS, false, "hill groundcover")
	WorkerThreadPool.wait_for_group_task_completion(group)
	for result in results:
		var chunk: Chunk = result
		if chunk.count > 0:
			distant_chunks.append(chunk)
			distant_clumps += chunk.count


func _plan_distant_chunk(origin: Vector2, rng: RandomNumberGenerator) -> Chunk:
	var chunk := Chunk.new()
	chunk.origin = origin
	var quality_scale := lerpf(0.55, 1.0, clampf(density, 0.0, 1.0))
	var candidates := ceili(DISTANT_CHUNK_SIZE * DISTANT_CHUNK_SIZE * 0.62 * quality_scale)
	var floats := PackedFloat32Array()
	floats.resize(candidates * FLOATS_PER_INSTANCE)
	var min_pos := Vector3(INF, INF, INF)
	var max_pos := Vector3(-INF, -INF, -INF)
	for i in candidates:
		var p := origin + Vector2(rng.randf(), rng.randf()) * DISTANT_CHUNK_SIZE
		var radius := p.length()
		var band := smoothstep(120.0, 205.0, radius) * (1.0 - smoothstep(655.0, DISTANT_EXTENT, radius))
		var frequency := lerpf(0.62, 0.15, smoothstep(180.0, 650.0, radius))
		if rng.randf() > band * frequency / 0.62:
			continue
		var y := field.surface_height(p.x, p.y)
		var slope := field.surface_slope(p.x, p.y)
		if y < TerrainField.WATER_LEVEL + 0.05 or rng.randf() < smoothstep(0.48, 0.95, slope):
			continue
		var cover := field.woodland_cover(p.x, p.y)
		var dry := field.dryness(p.x, p.y)
		var size := lerpf(2.8, 4.4, smoothstep(200.0, 650.0, radius)) / sqrt(quality_scale)
		var height := field.meadow_height(p.x, p.y) * rng.randf_range(0.95, 1.65) * lerpf(1.15, 0.78, cover)
		var yaw := rng.randf() * TAU
		var c := cos(yaw)
		var s := sin(yaw)
		var mature := meadow_senescence(dry, cover, patches.get_noise_2d(p.x, p.y))
		var tint := _blade_tint(mature, cover, rng.randf())
		var offset := chunk.count * FLOATS_PER_INSTANCE
		floats[offset] = c * size
		floats[offset + 2] = s * size
		floats[offset + 3] = p.x
		floats[offset + 5] = height
		floats[offset + 7] = y - 0.025
		floats[offset + 8] = -s * size
		floats[offset + 10] = c * size
		floats[offset + 11] = p.y
		floats[offset + 12] = tint.r
		floats[offset + 13] = tint.g
		floats[offset + 14] = tint.b
		floats[offset + 15] = tint.a
		floats[offset + 16] = rng.randf()
		floats[offset + 17] = c * 0.5 + 0.5
		floats[offset + 18] = s * 0.5 + 0.5
		floats[offset + 19] = rng.randf_range(0.12, 0.36)
		chunk.count += 1
		min_pos = min_pos.min(Vector3(p.x, y, p.y))
		max_pos = max_pos.max(Vector3(p.x, y + height, p.y))
	floats.resize(chunk.count * FLOATS_PER_INSTANCE)
	chunk.buffer = floats
	if chunk.count > 0:
		chunk.aabb = AABB(min_pos - Vector3(4.0, 0.1, 4.0), max_pos - min_pos + Vector3(8.0, 0.7, 8.0))
	return chunk


## Stable metre-scale stands shared by the sward, tussocks and seed stems.
## The field has no chunk/quality boundaries and continues into distant cover.
static func meadow_patch_noise() -> FastNoiseLite:
	var noise := FastNoiseLite.new()
	noise.seed = 70439
	noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise.frequency = 0.18
	noise.fractal_type = FastNoiseLite.FRACTAL_FBM
	noise.fractal_octaves = 3
	return noise


static func meadow_senescence(dry: float, cover: float, patch: float) -> float:
	# Keep a living green component even in drier stands. Wider noise thresholds
	# blend their boundaries, rather than dividing green turf from ripe crop.
	return clampf(0.10 + dry * 0.42 + smoothstep(-0.42, 0.50, patch) * 0.38 - cover * 0.30, 0.0, 0.72)


## Moisture/shade establish the palette; maturity is carried in alpha for
## the shader's green-base/straw-tip blend. Fine variation is deliberately small.
static func _blade_tint(dry: float, cover: float, variation: float) -> Color:
	var lush := Color(0.82, 1.0, 0.90)
	var straw := Color(1.06, 0.99, 0.89)
	var c := lush.lerp(straw, dry)
	c = c.lerp(Color(0.72, 0.86, 0.82), cover * 0.55)
	c = c * (0.92 + 0.16 * variation)
	c.a = dry
	return c


## A stand of reeds: tall thin blades and a couple of cattail stalks with
## brown heads, sharing the grass shader (UV/UV2 conventions as clump_mesh).
## Heights are fractions of the instance's y scale.
static func reed_mesh(seed_value := 91) -> ArrayMesh:
	var mb := MeshBuilder.new()
	mb.use_tangents = false
	mb.use_uv2 = true
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var blades := 8
	for b in blades + 2:
		var is_cattail := b >= blades
		var yaw := float(b) / float(blades + 2) * TAU + rng.randf_range(-0.3, 0.3)
		var offset := Vector3(cos(yaw), 0.0, sin(yaw)) * rng.randf_range(0.02, 0.12)
		var height := rng.randf_range(0.7, 1.0) if not is_cattail else rng.randf_range(0.85, 1.0)
		var lean := Vector3(cos(yaw), 0.0, sin(yaw)) * rng.randf_range(0.02, 0.14)
		var facing := yaw + rng.randf_range(-0.6, 0.6)
		var right := Vector3(cos(facing), 0.0, sin(facing))
		var normal := right.cross(Vector3.UP).normalized()
		var uv2 := Vector2(rng.randf(), height)
		var half_w := (0.011 if not is_cattail else 0.007) * rng.randf_range(0.8, 1.2)
		var segments := 5
		var rows: Array[Array] = []
		var stalk_top := height * (0.82 if is_cattail else 1.0)
		for i in range(segments + 1):
			var v := float(i) / float(segments)
			var taper := 1.0 - v * (0.75 if not is_cattail else 0.3)
			var p := offset + Vector3(0.0, v * stalk_top, 0.0) + lean * (v * v * height)
			var l := mb.add_vertex(p - right * (half_w * taper), normal, Vector2(0.0, v * (1.0 if not is_cattail else 0.8)),
					Color.WHITE, Vector4(1, 0, 0, 1), Color(0, 0, 0, 0), uv2)
			var r := mb.add_vertex(p + right * (half_w * taper), normal, Vector2(1.0, v * (1.0 if not is_cattail else 0.8)),
					Color.WHITE, Vector4(1, 0, 0, 1), Color(0, 0, 0, 0), uv2)
			rows.append([l, r])
		for i in range(segments):
			var a: int = rows[i][0]
			var bb: int = rows[i][1]
			var c: int = rows[i + 1][1]
			var d: int = rows[i + 1][0]
			mb.add_triangle(a, d, c)
			mb.add_triangle(a, c, bb)
		if not is_cattail:
			var tip_p := offset + Vector3(0.0, height, 0.0) + lean * height
			var tip := mb.add_vertex(tip_p, normal, Vector2(0.5, 1.0), Color.WHITE, Vector4(1, 0, 0, 1), Color(0, 0, 0, 0), uv2)
			var last: Array = rows[segments]
			mb.add_triangle(last[0], tip, last[1])
		else:
			# Cattail head: a short brown sausage on top of the stalk.
			var base := offset + Vector3(0.0, stalk_top, 0.0) + lean * height
			var top := base + Vector3(0.0, height * 0.17, 0.0)
			var brown := Color.WHITE
			var first := mb.vertex_count()
			mb.add_tube([base, base + Vector3.UP * 0.012, top - Vector3.UP * 0.012, top], [0.008, 0.018, 0.017, 0.007], 14, brown, 1.0, 1.0, 0.0, true)
			for i in range(first, mb.vertex_count()):
				mb.uvs[i] = Vector2(0.5, 1.0)
				mb.uv2s[i] = Vector2(-1, 1)
	return mb.commit()


## Reads one instance transform back out of a buffer (used by tests).
static func read_transform(buffer: PackedFloat32Array, index: int) -> Transform3D:
	var o := index * FLOATS_PER_INSTANCE
	var basis := Basis(
			Vector3(buffer[o + 0], buffer[o + 4], buffer[o + 8]),
			Vector3(buffer[o + 1], buffer[o + 5], buffer[o + 9]),
			Vector3(buffer[o + 2], buffer[o + 6], buffer[o + 10]))
	return Transform3D(basis, Vector3(buffer[o + 3], buffer[o + 7], buffer[o + 11]))


## A clump of tapered blades sharing one instance. Blade width is in metres
## (the instance scales x/z by 1); heights are fractions of the instance's
## y scale. UV.x runs across a blade, UV.y from root (0) to tip (1).
## UV2 = (per-blade random, local vertical derivative per UV.y). Vertex colour stays white
## because Godot multiplies it with the instance colour.
static func clump_mesh(blades := 7, segments := 5, width := 0.018, seed_value := 77,
		spread := 1.0, arch := 1.0, droop := 0.0) -> ArrayMesh:
	var mb := MeshBuilder.new()
	mb.use_tangents = false
	mb.use_uv2 = true
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	for b in blades:
		var yaw := float(b) / float(blades) * TAU + rng.randf_range(-0.4, 0.4)
		var offset := Vector3(cos(yaw), 0.0, sin(yaw)) * rng.randf_range(0.0, 0.07) * spread
		var height := rng.randf_range(0.55, 1.0)
		var lean := Vector3(cos(yaw), 0.0, sin(yaw)) * rng.randf_range(0.05, 0.28) * arch
		var curve := lean - Vector3.UP * clampf(droop, 0.0, 0.40)
		var facing := yaw + rng.randf_range(-0.7, 0.7)
		var right := Vector3(cos(facing), 0.0, sin(facing))
		var blade_random := rng.randf()
		var half_w := width * rng.randf_range(0.7, 1.15) * 0.5
		var rows: Array[Array] = []
		for i in range(segments + 1):
			var v := float(i) / float(segments + 1)
			var taper := 1.0 - v * 0.8
			var p := offset + Vector3(0.0, v * height, 0.0) + curve * (v * v * height)
			# The width direction crossed with the curved centre-line tangent.
			# Taper adds a parallel width component, which cancels in the cross.
			var normal := right.cross(Vector3.UP + curve * (2.0 * v)).normalized()
			var uv2 := Vector2(blade_random, height * (1.0 + curve.y * 2.0 * v))
			var l := mb.add_vertex(p - right * (half_w * taper), normal, Vector2(0.0, v), Color.WHITE,
					Vector4(1, 0, 0, 1), Color(0, 0, 0, 0), uv2)
			var r := mb.add_vertex(p + right * (half_w * taper), normal, Vector2(1.0, v), Color.WHITE,
					Vector4(1, 0, 0, 1), Color(0, 0, 0, 0), uv2)
			rows.append([l, r])
		var tip_p := offset + Vector3(0.0, height, 0.0) + curve * height
		var tip_normal := right.cross(Vector3.UP + curve * 2.0).normalized()
		var tip := mb.add_vertex(tip_p, tip_normal, Vector2(0.5, 1.0), Color.WHITE,
				Vector4(1, 0, 0, 1), Color(0, 0, 0, 0), Vector2(blade_random, height * (1.0 + curve.y * 2.0)))
		for i in range(segments):
			var a: int = rows[i][0]
			var bb: int = rows[i][1]
			var c: int = rows[i + 1][1]
			var d: int = rows[i + 1][0]
			# Viewed from the front: a bottom-left, bb bottom-right, c top-right,
			# d top-left. Clockwise: a -> d -> c -> bb.
			mb.add_triangle(a, d, c)
			mb.add_triangle(a, c, bb)
		var last: Array = rows[segments]
		mb.add_triangle(last[0], tip, last[1])
	# Keep every blade at every LOD. General mesh decimation removes whole
	# blades because their borders prevent ordinary edge collapse.
	var mesh := ArrayMesh.new()
	var lods := {}
	if segments >= 5:
		lods = {
			0.006: clump_lod_indices(blades, segments, PackedInt32Array([0, 2, 4])),
			0.018: clump_lod_indices(blades, segments, PackedInt32Array([0, 3])),
			0.05: clump_lod_indices(blades, segments, PackedInt32Array([0])),
		}
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, mb._arrays(), [], lods, mb._format_flags())
	return mesh


static func clump_lod_indices(blades: int, segments: int, retained_rows: PackedInt32Array) -> PackedInt32Array:
	var result := PackedInt32Array()
	var stride := (segments + 1) * 2 + 1
	for blade in blades:
		var base := blade * stride
		for i in range(retained_rows.size() - 1):
			var a := base + retained_rows[i] * 2
			var b := base + retained_rows[i + 1] * 2
			result.append_array(PackedInt32Array([a, b, b + 1, a, b + 1, a + 1]))
		var last := base + retained_rows[-1] * 2
		result.append_array(PackedInt32Array([last, base + stride - 1, last + 1]))
	return result
