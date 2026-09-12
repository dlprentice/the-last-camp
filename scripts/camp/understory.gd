class_name Understory
extends Node3D

## Grass blades (chunked MultiMeshes), ferns under the canopy, shrubs from the
## scene plan and wildflowers through the clearing.

const FERN_COUNT := 2600
## Plants are batched per cell so culling works on the parts of the woodland in
## view, and only cells near the camera cast sun shadows (distant plant shadows
## are invisible but cost a full pass per cascade).
const FERN_CELL := 16.0
const FERN_SHADOW_RANGE := 40.0
const SHRUB_SHADOW_RANGE := 60.0
## Card bushes near the camp were replaced by photoscanned shrubs (see
## ScannedDressing._place_plan_shrubs); flip to compare the old look.
const CARD_SHRUBS_NEAR := false
const FLOWER_COUNT := 1500

var field: TerrainField
var plan: ScenePlan
var grass_material: ShaderMaterial
var distant_grass_material: ShaderMaterial
var tussock_material: ShaderMaterial
var fern_material: ShaderMaterial
var shrub_material: ShaderMaterial
var flower_material: ShaderMaterial
var reed_material: ShaderMaterial
var grass_chunks: Array[MultiMeshInstance3D] = []
var fern_cells: Array[MultiMeshInstance3D] = []
var meadow_cells: Array[MultiMeshInstance3D] = []
## Twin cell pairs: {near = caster, far = non-caster, half = bounds half-diagonal}.
var shadow_cells: Array[Dictionary] = []
var shrub_mesh: ArrayMesh
var blade_count := 0
var distant_clump_count := 0
var distant_shrub_count := 0
var _rng := RandomNumberGenerator.new()
var _grass_density := 1.0
var _grass_distance := 70.0
var _foliage_distance := 1.0
var _replant_thread: Thread
var _replant_dirty := false


func _init(p_field: TerrainField, p_plan: ScenePlan) -> void:
	field = p_field
	plan = p_plan
	name = "Understory"
	_rng.seed = 5150


## Ferns, shrubs and flowers (fast enough for the main thread).
func build_plants() -> void:
	_grass_density = Quality.current.grass_density
	_grass_distance = Quality.current.grass_distance
	_build_ferns()
	_build_shrubs()
	_build_flowers()
	_build_reeds()
	_build_distant_shrubs()
	Quality.preset_changed.connect(apply_quality)
	apply_quality(Quality.current)


## Uploads a planned grass layout (planning itself is threadable).
func add_grass(planter: GrassPlanter) -> void:
	for c in grass_chunks:
		c.queue_free()
	grass_chunks.clear()
	blade_count = 0
	if grass_material == null:
		grass_material = ShaderMaterial.new()
		grass_material.shader = load("res://shaders/grass.gdshader")
		apply_quality(Quality.current)
	var blade := GrassPlanter.clump_mesh()
	_upload_grass(planter.chunks, blade, grass_material, "Grass")
	if distant_grass_material == null:
		distant_grass_material = ShaderMaterial.new()
		distant_grass_material.shader = grass_material.shader
		# The terrain's physical edge ends this vegetation, not a camera ring.
		distant_grass_material.set_shader_parameter("fade_start", 730.0)
		distant_grass_material.set_shader_parameter("fade_end", 800.0)
	var distant_blade := GrassPlanter.clump_mesh(5, 1, 0.055, 901)
	_upload_grass(planter.distant_chunks, distant_blade, distant_grass_material, "HillGrass")
	blade_count = planter.total_blades
	distant_clump_count = planter.distant_clumps
	print("Groundcover hills: %d clumps in %d batches, %d shrubs, extent %.0f m" % [
		distant_clump_count, planter.distant_chunks.size(), distant_shrub_count, GrassPlanter.DISTANT_EXTENT])


func _upload_grass(chunks: Array[GrassPlanter.Chunk], mesh: ArrayMesh,
		material: ShaderMaterial, prefix: String) -> void:
	for chunk in chunks:
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.use_colors = true
		mm.use_custom_data = true
		mm.mesh = mesh
		mm.instance_count = chunk.count
		mm.buffer = chunk.buffer
		mm.custom_aabb = chunk.aabb
		var mmi := MultiMeshInstance3D.new()
		mmi.name = "%s_%d_%d" % [prefix, int(chunk.origin.x), int(chunk.origin.y)]
		mmi.multimesh = mm
		mmi.material_override = material
		mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		mmi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
		mmi.layers = Pond.GRASS_LAYER
		_set_range(mmi, _grass_distance)
		add_child(mmi)
		grass_chunks.append(mmi)


## Low, irregular woodland undergrowth continues the existing shrub species.
## Reuse its reviewed mesh and a separate RNG so the campsite plants stay put.
func _build_distant_shrubs() -> void:
	var mesh: ArrayMesh = shrub_mesh
	var material := shrub_material.duplicate() as ShaderMaterial
	material.set_shader_parameter("fade_start", 730.0)
	material.set_shader_parameter("fade_end", 800.0)
	var rng := RandomNumberGenerator.new()
	rng.seed = 73109
	var groups := {}
	for attempt in 4200:
		var angle := rng.randf() * TAU
		var radius := sqrt(lerpf(130.0 * 130.0, 650.0 * 650.0, rng.randf()))
		var p := Vector2(cos(angle), sin(angle)) * radius
		var cover := field.woodland_cover(p.x, p.y)
		if rng.randf() > cover * 0.70 * smoothstep(130.0, 190.0, radius):
			continue
		var y := field.surface_height(p.x, p.y)
		if y < TerrainField.WATER_LEVEL + 0.2 or field.surface_slope(p.x, p.y) > 0.68:
			continue
		var key := Vector2i(floori(p.x / 64.0), floori(p.y / 64.0))
		if not groups.has(key):
			groups[key] = {transforms = [], customs = []}
		var size := rng.randf_range(0.9, 1.65)
		var basis := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(size, size * rng.randf_range(0.75, 1.15), size))
		groups[key].transforms.append(Transform3D(basis, Vector3(p.x, y - 0.06, p.y)))
		var tint := Color(0.88, 0.98, 0.82).lerp(Color(1.0, 0.95, 0.78), rng.randf() * 0.55)
		groups[key].customs.append(Color(rng.randf(), tint.r, tint.g, tint.b))
		distant_shrub_count += 1
	for key: Vector2i in groups:
		var transforms: Array[Transform3D] = []
		var customs: Array[Color] = []
		transforms.assign(groups[key].transforms)
		customs.assign(groups[key].customs)
		var node := _add_multimesh("HillShrubs_%d_%d" % [key.x, key.y], mesh, material, transforms, customs)
		node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		var bounds: AABB = transforms[0] * mesh.get_aabb().grow(0.45)
		for transform in transforms:
			bounds = bounds.merge(transform * mesh.get_aabb().grow(0.45))
		node.multimesh.custom_aabb = bounds


# --------------------------------------------------------------------- ferns

func _build_ferns() -> void:
	fern_material = _card_material("fern", 0.42, 0.85, 0.55)
	var mesh := _fern_mesh()
	var transforms: Array[Transform3D] = []
	var customs: Array[Color] = []
	var attempts := 0
	while transforms.size() < FERN_COUNT and attempts < FERN_COUNT * 12:
		attempts += 1
		var a := _rng.randf() * TAU
		var r := lerpf(14.0, 78.0, sqrt(_rng.randf()))
		var p := Vector2(cos(a) * r, sin(a) * r)
		var cover := field.canopy.sample(p.x, p.y)
		if _rng.randf() > cover * 1.3 - 0.1:
			continue
		if not _plantable(p, 1.2):
			continue
		var y := field.height(p.x, p.y)
		var s := _rng.randf_range(0.55, 1.35) * lerpf(0.8, 1.15, cover)
		transforms.append(Transform3D(Basis(Vector3.UP, _rng.randf() * TAU).scaled(Vector3.ONE * s), Vector3(p.x, y - 0.03, p.y)))
		var tint := Color(0.9, 1.0, 0.85).lerp(Color(1.0, 0.95, 0.7), _rng.randf() * 0.35)
		customs.append(Color(_rng.randf(), tint.r, tint.g, tint.b))
	_add_cells("Ferns", mesh, fern_material, transforms, customs, FERN_SHADOW_RANGE)


## A rosette of arching fronds, each a curved strip mapped to one atlas cell.
func _fern_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var fronds := 7
	for f in fronds:
		var yaw := float(f) / float(fronds) * TAU + _rng.randf_range(-0.2, 0.2)
		var dir := Vector3(cos(yaw), 0.0, sin(yaw))
		var length := _rng.randf_range(0.75, 1.05)
		var width := length * 0.42
		var tilt := _rng.randf_range(0.0, 0.25)
		var cell := Vector2i(_rng.randi() % 2, _rng.randi() % 2)
		var rect := Rect2(float(cell.x) * 0.5, float(cell.y) * 0.5, 0.5, 0.5)
		var side := dir.cross(Vector3.UP).normalized() * (width * 0.5)
		# The atlas frond grows from a real rachis, connected to the crown.
		var stem_points: Array[Vector3] = [Vector3(0, 0.006, 0)]
		var stem_radii: Array[float] = [0.004]
		for step in 13:
			var t := float(step) / 12.0
			stem_points.append(dir * length * (0.15 + 0.85 * t) + Vector3.UP * (length * (t - 0.72 * t * t) + tilt + 0.05))
			stem_radii.append(lerpf(0.003, 0.0006, t))
		var stem_start := mb.vertex_count()
		mb.add_tube(stem_points, stem_radii, 5, Color(0.4, 0.5, 0, 1), 1, 1, 0, true)
		for vertex in range(stem_start, mb.vertex_count()):
			mb.uvs[vertex] = Vector2(-1, -1)
			mb.colors[vertex].r = clampf(mb.vertices[vertex].length() / length, 0, 1)
		var segments := 8
		var prev_l := -1
		var prev_r := -1
		for i in range(segments + 1):
			var t := float(i) / float(segments)
			var horiz := length * (0.15 + 0.85 * t)
			var vert := length * (1.0 * t - 0.72 * t * t) + tilt + 0.05
			var p := dir * horiz + Vector3(0.0, vert, 0.0)
			var v := rect.position.y + rect.size.y * (1.0 - t)
			var n := Vector3.UP
			var tangent := Vector4(side.normalized().x, side.normalized().y, side.normalized().z, 1.0)
			var color := Color(t, _rng.randf(), 0.0, 1.0)
			var l := mb.add_vertex(p - side, n, Vector2(rect.position.x, v), color, tangent)
			var r := mb.add_vertex(p + side, n, Vector2(rect.end.x, v), color, tangent)
			if prev_l >= 0:
				# Viewed from above with the frond growing away: prev_l, prev_r
				# near, l, r far. Clockwise from above: prev_l -> l -> r -> prev_r.
				mb.add_triangle(prev_l, l, r)
				mb.add_triangle(prev_l, r, prev_r)
			prev_l = l
			prev_r = r
	mb.recompute_normals()
	return mb.commit()


# -------------------------------------------------------------------- shrubs

func _build_shrubs() -> void:
	# Bush cards are pale; against a low sun a strong backlight turned them
	# into glowing white leaves in the after-rain shot, so they transmit less.
	shrub_material = _card_material("bush", 0.45, 0.18, 0.6)
	var mesh := _shrub_mesh()
	var transforms: Array[Transform3D] = []
	var customs: Array[Color] = []
	for s in plan.shrubs:
		var y := field.height(s.position.x, s.position.y)
		transforms.append(Transform3D(Basis(Vector3.UP, s.rotation).scaled(Vector3.ONE * s.scale), Vector3(s.position.x, y - 0.05, s.position.y)))
		# The bush atlas is pale; a darker, greener tint keeps the cards from
		# reading as bleached against the sky.
		var tint := Color(0.62, 0.72, 0.56).lerp(Color(0.70, 0.66, 0.48), _rng.randf() * 0.4)
		customs.append(Color(_rng.randf(), tint.r, tint.g, tint.b))
	shrub_mesh = mesh
	# The planned bushes around the camp are placed as photoscanned shrubs by
	# ScannedDressing; the card mesh and material remain for the distant hills.
	if CARD_SHRUBS_NEAR:
		_add_cells("Shrubs", mesh, shrub_material, transforms, customs, SHRUB_SHADOW_RANGE, true)


func _shrub_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var cards := 21
	for c in cards:
		var yaw := float(c) / float(cards) * TAU + _rng.randf_range(-0.3, 0.3)
		var out := Vector3(cos(yaw), 0.0, sin(yaw))
		var pos := out * _rng.randf_range(0.15, 0.5) + Vector3(0.0, _rng.randf_range(0.08, 0.40), 0.0)
		var size := _rng.randf_range(0.62, 0.86)
		var up := (Vector3.UP + out * _rng.randf_range(-0.2, 0.35)).normalized()
		var right := up.cross(out)
		if right.length_squared() < 1e-4:
			right = Vector3.RIGHT
		right = right.normalized()
		var cell := Vector2i(_rng.randi() % 2, _rng.randi() % 2)
		var rect := Rect2(float(cell.x) * 0.5, float(cell.y) * 0.5, 0.5, 0.5)
		var w := size * 0.9
		var p0 := pos - right * (w * 0.5)
		var p1 := pos + right * (w * 0.5)
		var color := Color(0.15, _rng.randf(), 0.0, 1.0)
		var color_top := Color(1.0, color.g, 0.0, 1.0)
		var n := (p1 - p0).cross(up).normalized()
		var tangent := Vector4(right.x, right.y, right.z, 1.0)
		var a := mb.add_vertex(p0, n, Vector2(rect.position.x, rect.end.y), color, tangent)
		var b := mb.add_vertex(p1, n, Vector2(rect.end.x, rect.end.y), color, tangent)
		var cc := mb.add_vertex(p1 + up * size, n, Vector2(rect.end.x, rect.position.y), color_top, tangent)
		var d := mb.add_vertex(p0 + up * size, n, Vector2(rect.position.x, rect.position.y), color_top, tangent)
		mb.add_quad_indices(a, b, cc, d)
	return mb.commit()


# ------------------------------------------------------------------- flowers

func _build_flowers() -> void:
	flower_material = ShaderMaterial.new()
	flower_material.shader = load("res://shaders/meadow_plant.gdshader")
	var groups: Array[Array] = []
	var phases: Array[Array] = []
	for i in 12:
		groups.append([])
		phases.append([])
	var clusters: Array[Dictionary] = [
		{centre = Vector2(5.5, 8.0), species = MeadowPlants.Kind.YARROW, radius = 1.3},
		{centre = Vector2(7.2, 6.4), species = MeadowPlants.Kind.BUTTERCUP, radius = 1.6},
		{centre = Vector2(-8.0, 6.6), species = MeadowPlants.Kind.DAISY, radius = 1.5},
	]
	for i in 52:
		var a := _rng.randf() * TAU
		var r := lerpf(4.0, 39.0, sqrt(_rng.randf()))
		clusters.append({centre = Vector2(cos(a) * r, sin(a) * r), species = _rng.randi() % 4, radius = _rng.randf_range(1.0, 2.8)})
	var count := 0
	for attempt in FLOWER_COUNT * 10:
		if count >= FLOWER_COUNT:
			break
		var cluster: Dictionary = clusters[_rng.randi() % clusters.size()]
		var p: Vector2 = cluster.centre + Vector2(_rng.randf_range(-1, 1), _rng.randf_range(-1, 1)) * cluster.radius
		if not _plantable(p, 0.45) or field.canopy.sample(p.x, p.y) > 0.3:
			continue
		if field.grass_suitability(p.x, p.y) < 0.6:
			continue
		var species: int = cluster.species
		# Clover stays low in shorter sward; the taller flowers rise above it.
		if species == MeadowPlants.Kind.CLOVER and field.meadow_height(p.x, p.y) > 0.37:
			continue
		var scale_h := _rng.randf_range(0.80, 1.22)
		var group := species * 3 + _rng.randi() % 3
		groups[group].append(Transform3D(Basis(Vector3.UP, _rng.randf() * TAU).scaled(Vector3(1, scale_h, 1)), Vector3(p.x, field.height_fast(p.x, p.y) - 0.008, p.y)))
		phases[group].append(Color(_rng.randf(), 1, 1, 1))
		count += 1
	for group in 12:
		var species := group / 3
		var variant := group % 3
		var transforms: Array[Transform3D] = []
		var customs: Array[Color] = []
		transforms.assign(groups[group])
		customs.assign(phases[group])
		var node_name := "Meadow_%d" % species + ("" if variant == 0 else "_variant_%d" % variant)
		_add_multimesh(node_name, MeadowPlants.flower(species, 904 + species + variant * 311), flower_material, transforms, customs)
	_build_meadow_grasses()


## Basal tufts and fine seed stems share their habitat and root positions.
## Hairgrass-like open panicles favour the green moist stands; compact seed
## heads and older straw are more common in the drier openings. These are
## geometric growth forms, rather than a claim to identify the reference species.
func _build_meadow_grasses() -> void:
	tussock_material = ShaderMaterial.new()
	tussock_material.shader = load("res://shaders/grass.gdshader")
	tussock_material.set_shader_parameter("sheen", 0.12)
	# Broad radial tufts already have their real footprint; only the tiny base
	# sward needs distance widening to maintain apparent blade coverage.
	tussock_material.set_shader_parameter("distance_widen", 0.0)
	var tufts: Array[ArrayMesh] = [
		GrassPlanter.clump_mesh(24, 5, 0.011, 4021, 3.0, 2.15, 0.18),
		GrassPlanter.clump_mesh(18, 5, 0.020, 4059, 3.2, 2.5, 0.26),
	]
	var heads: Array[ArrayMesh] = [MeadowPlants.seed_heads(0, 4021), MeadowPlants.seed_heads(1, 4059)]
	var groups := _plan_meadow_grasses()
	for key: Vector3i in groups:
		var group: Dictionary = groups[key]
		var transforms: Array[Transform3D] = []
		var customs: Array[Color] = []
		var colors: Array[Color] = []
		transforms.assign(group.transforms)
		customs.assign(group.customs)
		colors.assign(group.colors)
		var suffix := "%d_%d_%d" % [key.x, key.y, key.z]
		var tuft := _add_multimesh("MeadowTussocks_" + suffix, tufts[key.z], tussock_material, transforms, customs, false, colors)
		tuft.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_set_meadow_bounds(tuft, transforms)
		_set_range(tuft, _grass_distance)
		meadow_cells.append(tuft)
		transforms.assign(group.seed_transforms)
		customs.assign(group.seed_customs)
		if not transforms.is_empty():
			var seed_stems := _add_multimesh("MeadowSeedHeads_" + suffix, heads[key.z], flower_material, transforms, customs)
			seed_stems.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			_set_meadow_bounds(seed_stems, transforms)
			_set_range(seed_stems, 110.0 * _foliage_distance)
			fern_cells.append(seed_stems)


## Pure placement stage, also exercised by the existing groundcover tests.
## Independent RNG does not couple tuft density to the flower/fern layout.
func _plan_meadow_grasses(attempts := 26000, stand_extent := 96.0) -> Dictionary:
	var rng := RandomNumberGenerator.new()
	rng.seed = 82317
	var patches := GrassPlanter.meadow_patch_noise()
	var groups := {}
	for i in attempts:
		var p := Vector2(rng.randf_range(-stand_extent, stand_extent), rng.randf_range(-stand_extent, stand_extent))
		var radius := p.length()
		if radius > stand_extent or not _plantable(p, 0.65):
			continue
		var cover := field.woodland_cover(p.x, p.y)
		var patch := patches.get_noise_2d(p.x, p.y)
		var colony := lerpf(0.30, 1.0, smoothstep(-0.40, 0.25, patch))
		var edge := 1.0 - smoothstep(stand_extent * 0.72, stand_extent, radius)
		if rng.randf() > colony * lerpf(0.95, 0.08, cover) * edge:
			continue
		var width := rng.randf_range(0.68, 1.08)
		# The widest arched mesh reaches 0.93 m from its root at unit scale.
		# Include room for the shader's ordinary wind deformation at the path.
		if not _meadow_footprint_clear(p, width * 0.96 + 0.12):
			continue
		var dry := field.dryness(p.x, p.y)
		var mature := GrassPlanter.meadow_senescence(dry, cover, patch)
		# Contiguous plant communities, with limited overlap at their edges.
		var form := 0 if patch + dry * 0.45 < 0.12 else 1
		var h := rng.randf_range(0.40, 0.88) * lerpf(1.0, 0.80, cover)
		var yaw := rng.randf() * TAU
		var at := Vector3(p.x, field.height_fast(p.x, p.y) - 0.018, p.y)
		var transform := Transform3D(Basis(Vector3.UP, yaw).scaled(Vector3(width, h, width)), at)
		var key := Vector3i(floori(p.x / 16.0), floori(p.y / 16.0), form)
		if not groups.has(key):
			groups[key] = {transforms = [], customs = [], colors = [], seed_transforms = [], seed_customs = []}
		var phase := rng.randf()
		groups[key].transforms.append(transform)
		groups[key].customs.append(Color(phase, cos(yaw) * 0.5 + 0.5, sin(yaw) * 0.5 + 0.5, rng.randf_range(0.10, 0.34)))
		# Basal tufts retain younger green leaves above the older short sward.
		groups[key].colors.append(GrassPlanter._blade_tint(maxf(mature - 0.22, 0.0), cover, rng.randf()))
		if rng.randf() < lerpf(0.38, 0.80, mature) * (1.0 - cover * 0.65):
			var seed_scale := Vector3(width, rng.randf_range(0.55, 0.92), width)
			groups[key].seed_transforms.append(Transform3D(Basis(Vector3.UP, yaw + 0.4).scaled(seed_scale), at))
			var tint := Color(0.85, 1.0, 0.82).lerp(Color(1.03, 0.91, 0.69), mature)
			groups[key].seed_customs.append(Color(phase, tint.r, tint.g, tint.b))
	return groups


func _meadow_footprint_clear(p: Vector2, radius: float) -> bool:
	# Distance to the path is 1-Lipschitz, so this protects the whole circular
	# footprint, including points between the sampled habitat boundary checks.
	if field.walking_distance(p) < radius + 0.56 or TerrainField.camp_wear(p) > 0.08:
		return false
	for side in 16:
		var angle := float(side) * TAU / 16.0
		var q := p + Vector2(cos(angle), sin(angle)) * radius
		if field.walking_distance(q) < 0.56 or TerrainField.camp_wear(q) > 0.08:
			return false
		if field.height_fast(q.x, q.y) < TerrainField.WATER_LEVEL + 0.10:
			return false
	return true


## Upload one placement set as 16 m cells. Each cell exists twice with the same
## instances: a shadow caster visible while the camera is within shadow_range and
## a non-caster visible from there to the shader fade, so the geometry is drawn
## once at any distance and only nearby plants enter the shadow cascades.
func _add_cells(prefix: String, mesh: ArrayMesh, material: Material, transforms: Array[Transform3D],
		customs: Array[Color], shadow_range: float, reflects := false) -> void:
	var cells := {}
	for i in transforms.size():
		var origin := transforms[i].origin
		var key := Vector2i(floori(origin.x / FERN_CELL), floori(origin.z / FERN_CELL))
		if not cells.has(key):
			cells[key] = {transforms = [], customs = []}
		cells[key].transforms.append(transforms[i])
		cells[key].customs.append(customs[i])
	for key: Vector2i in cells:
		var cell_transforms: Array[Transform3D] = []
		var cell_customs: Array[Color] = []
		cell_transforms.assign(cells[key].transforms)
		cell_customs.assign(cells[key].customs)
		var near := _add_multimesh("%s_%d_%d" % [prefix, key.x, key.y], mesh, material, cell_transforms, cell_customs, reflects)
		_set_meadow_bounds(near, cell_transforms)
		var half := near.multimesh.custom_aabb.size.length() * 0.5 + 2.0
		near.visibility_range_end = shadow_range + half
		var far := _add_multimesh("%sFar_%d_%d" % [prefix, key.x, key.y], mesh, material, cell_transforms, cell_customs, reflects)
		far.multimesh.custom_aabb = near.multimesh.custom_aabb
		far.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		# A slight overlap draws both twins for one frame rather than neither.
		far.visibility_range_begin = shadow_range + half - 0.5
		far.visibility_range_end = 110.0 * _foliage_distance + half
		shadow_cells.append({near = near, far = far, half = half})


## Stop drawing a batch once the shader has dissolved all of it. The renderer
## measures the range to the batch bounds' centre, so the cull distance adds
## half the bounds' diagonal; the fade already finished well before that.
static func _set_range(node: MultiMeshInstance3D, fade_end: float) -> void:
	var bounds := node.multimesh.custom_aabb
	if bounds.size == Vector3.ZERO:
		bounds = node.multimesh.mesh.get_aabb()
	node.visibility_range_end = fade_end + bounds.size.length() * 0.5 + 2.0
	node.visibility_range_end_margin = 4.0


func _set_meadow_bounds(node: MultiMeshInstance3D, transforms: Array[Transform3D]) -> void:
	var local_bounds := node.multimesh.mesh.get_aabb().grow(0.28)
	var bounds := transforms[0] * local_bounds
	for transform in transforms:
		bounds = bounds.merge(transform * local_bounds)
	node.multimesh.custom_aabb = bounds


# --------------------------------------------------------------------- reeds

const REED_CLUMPS := 30

## Cattails and tall reeds in stands along the shallows, skipping the dock.
func _build_reeds() -> void:
	reed_material = ShaderMaterial.new()
	reed_material.shader = load("res://shaders/grass.gdshader")
	reed_material.set_shader_parameter("root_color", Color(0.09, 0.13, 0.05))
	reed_material.set_shader_parameter("tip_color", Color(0.4, 0.47, 0.18))
	reed_material.set_shader_parameter("dry_tip_color", Color(0.52, 0.44, 0.2))
	reed_material.set_shader_parameter("sheen", 0.2)
	reed_material.set_shader_parameter("trample_radius", 0.7)
	reed_material.set_shader_parameter("fade_start", 90.0)
	reed_material.set_shader_parameter("fade_end", 130.0)
	var mesh := GrassPlanter.reed_mesh()
	var transforms: Array[Transform3D] = []
	var customs: Array[Color] = []
	var colors: Array[Color] = []
	var dock_dir := (TerrainField.POND_CENTRE - TerrainField.DOCK_START).normalized()
	for c in REED_CLUMPS:
		var angle := _rng.randf() * TAU
		var centre := TerrainField.shore_point(angle)
		# Keep the dock approach and the beach by the trail clear.
		var to_dock := centre - TerrainField.DOCK_START
		if to_dock.length() < 5.0 or absf(to_dock.dot(Vector2(-dock_dir.y, dock_dir.x))) < 2.2 and to_dock.dot(dock_dir) > -1.0 and to_dock.dot(dock_dir) < 10.0:
			continue
		var radius := _rng.randf_range(1.4, 3.2)
		var count := int(radius * radius * _rng.randf_range(2.2, 3.4))
		for i in count:
			var p := centre + Vector2(_rng.randf_range(-1.0, 1.0), _rng.randf_range(-1.0, 1.0)) * radius
			var y := field.height_fast(p.x, p.y)
			var depth := TerrainField.WATER_LEVEL - y
			if depth > 0.42 or depth < -0.18:
				continue
			var s := _rng.randf_range(0.85, 1.15)
			var h := _rng.randf_range(1.15, 1.7) * (1.0 - clampf(depth, 0.0, 0.4) * 0.5)
			transforms.append(Transform3D(Basis(Vector3.UP, _rng.randf() * TAU).scaled(Vector3(s, h, s)), Vector3(p.x, y - 0.02, p.y)))
			var lean := _rng.randf() * TAU
			customs.append(Color(_rng.randf(), cos(lean) * 0.5 + 0.5, sin(lean) * 0.5 + 0.5, _rng.randf_range(0.05, 0.2)))
			var dry := _rng.randf_range(0.0, 0.6)
			colors.append(Color(_rng.randf_range(0.85, 1.05), _rng.randf_range(0.9, 1.05), _rng.randf_range(0.8, 1.0), dry))
	if transforms.is_empty():
		return
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.use_custom_data = true
	mm.mesh = mesh
	mm.instance_count = transforms.size()
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
		mm.set_instance_color(i, colors[i])
		mm.set_instance_custom_data(i, customs[i])
	var mmi := MultiMeshInstance3D.new()
	mmi.name = "Reeds"
	mmi.multimesh = mm
	mmi.material_override = reed_material
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	mmi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	add_child(mmi)


# ------------------------------------------------------------------- helpers

func _plantable(p: Vector2, clearance: float) -> bool:
	if field.is_underwater(p.x, p.y) or field.height(p.x, p.y) < TerrainField.WATER_LEVEL + 0.25:
		return false
	if field.walking_distance(p) < clearance + 0.55:
		return false
	if TerrainField.camp_wear(p) > 0.08:
		return false
	return true


func _card_material(atlas: String, cutoff: float, translucency: float, roughness: float) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/undergrowth.gdshader")
	Camp.bind_texture(mat, "albedo_tex", "res://textures/%s.png" % atlas)
	Camp.bind_texture(mat, "normal_trans_tex", "res://textures/%s_nt.png" % atlas)
	mat.set_shader_parameter("alpha_cutoff", cutoff)
	mat.set_shader_parameter("translucency", translucency)
	mat.set_shader_parameter("roughness", roughness)
	return mat


func _add_multimesh(node_name: String, mesh: ArrayMesh, material: Material,
		transforms: Array[Transform3D], customs: Array[Color], reflects := false, colors: Array[Color] = []) -> MultiMeshInstance3D:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.use_colors = not colors.is_empty()
	mm.mesh = mesh
	mm.instance_count = transforms.size()
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
		mm.set_instance_custom_data(i, customs[i])
		if mm.use_colors:
			mm.set_instance_color(i, colors[i])
	var mmi := MultiMeshInstance3D.new()
	mmi.name = node_name
	mmi.multimesh = mm
	mmi.material_override = material
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	mmi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	mmi.layers = 1 if reflects else Pond.GRASS_LAYER
	add_child(mmi)
	return mmi


func apply_quality(p: QualityPreset) -> void:
	_foliage_distance = p.foliage_distance
	for mat in [grass_material, tussock_material]:
		if mat != null:
			mat.set_shader_parameter("fade_start", p.grass_distance * 0.72)
			mat.set_shader_parameter("fade_end", p.grass_distance)
	for mat in [fern_material, shrub_material, flower_material]:
		if mat != null:
			mat.set_shader_parameter("fade_start", 70.0 * p.foliage_distance)
			mat.set_shader_parameter("fade_end", 110.0 * p.foliage_distance)
	for node in grass_chunks:
		_set_range(node, p.grass_distance)
	for node in meadow_cells:
		_set_range(node, p.grass_distance)
	for node in fern_cells:
		_set_range(node, 110.0 * p.foliage_distance)
	for pair in shadow_cells:
		var far: MultiMeshInstance3D = pair.far
		far.visibility_range_end = 110.0 * p.foliage_distance + pair.half
	if not is_equal_approx(p.grass_density, _grass_density) or not is_equal_approx(p.grass_distance, _grass_distance):
		_grass_density = p.grass_density
		_grass_distance = p.grass_distance
		_replant_grass_async()


## Quality changes re-plan the grass on a worker thread and swap it in. Only
## one plan runs at a time; changes made meanwhile queue a single re-run.
func _replant_grass_async() -> void:
	if _replant_thread != null:
		_replant_dirty = true
		return
	var planter := GrassPlanter.new(field, _grass_distance + GrassPlanter.CHUNK_SIZE, _grass_density)
	_replant_thread = Thread.new()
	_replant_thread.start(planter.plan)
	while _replant_thread != null and _replant_thread.is_alive():
		await get_tree().process_frame
	if _replant_thread == null or not is_inside_tree():
		return
	_replant_thread.wait_to_finish()
	_replant_thread = null
	add_grass(planter)
	if _replant_dirty:
		_replant_dirty = false
		_replant_grass_async()


## A plan still running when the scene is torn down must be joined, or its
## detached thread keeps touching freed memory during shutdown.
func _exit_tree() -> void:
	if _replant_thread != null and _replant_thread.is_started():
		_replant_thread.wait_to_finish()
	_replant_thread = null
