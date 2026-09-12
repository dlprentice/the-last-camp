class_name ScenePlan
extends RefCounted

## Deterministic layout of everything placed on the landscape: trees (species,
## seed, scale), boulders and shrubs. Also paints the canopy coverage into the
## terrain field so ground materials and grass respond to the tree cover.

class TreeEntry:
	var position: Vector2
	var kind: TreeSpecies.Kind
	var seed_value: int
	var scale: float
	var rotation: float
	var far := false

class RockEntry:
	var position: Vector2
	var scale: float
	var rotation: float
	var sink: float
	var variant: int

class ShrubEntry:
	var position: Vector2
	var scale: float
	var rotation: float

const NEAR_TREE_MIN_SPACING := 4.6
const FAR_TREE_MIN_SPACING := 5.5
const NEAR_LIMIT := 88.0
const FAR_LIMIT := 210.0
## Irregular woodland stands, with the wet western bank carrying several
## ages of broadleaf growth. Z is the patch radius, not a placement grid.
const WOODLAND_GROVES: Array[Vector3] = [
	Vector3(-61, -22, 13), Vector3(-73, 19, 15), Vector3(-66, 43, 17),
	Vector3(-43, -43, 14), Vector3(-96, -52, 20), Vector3(-117, 20, 22),
	Vector3(-108, 65, 18), Vector3(-29, 63, 15), Vector3(35, 67, 19),
	Vector3(61, 13, 20), Vector3(35, -46, 18), Vector3(84, -53, 21),
	Vector3(124, 35, 24), Vector3(129, -61, 23), Vector3(73, 111, 22),
	Vector3(-164, 74, 26), Vector3(-149, -101, 24), Vector3(80, -168, 25),
]

var trees: Array[TreeEntry] = []
var rocks: Array[RockEntry] = []
var shrubs: Array[ShrubEntry] = []

var _rng := RandomNumberGenerator.new()
var _field: TerrainField
var _grove_noise := FastNoiseLite.new()
var _intro_lane: Array[Vector2] = []


func _init(field: TerrainField, seed_value := 1337) -> void:
	_field = field
	_rng.seed = seed_value
	_grove_noise.seed = seed_value + 412
	_grove_noise.frequency = 0.034
	_grove_noise.fractal_octaves = 2
	for p in IntroDolly.PATH:
		_intro_lane.append(Vector2(p.x, p.z))


func build() -> void:
	_place_specimens()
	_place_treeline()
	_place_far_forest()
	_place_saplings()
	_place_rocks()
	_place_shrubs()
	_paint_canopy()
	_place_pond_enclosure()
	_place_dense_stands()
	_paint_canopy()


func near_trees() -> Array[TreeEntry]:
	return trees.filter(func(t: TreeEntry) -> bool: return not t.far)


func far_trees() -> Array[TreeEntry]:
	return trees.filter(func(t: TreeEntry) -> bool: return t.far)


# ------------------------------------------------------------------- trees

func _add_tree(pos: Vector2, kind: TreeSpecies.Kind, scale: float, far := false) -> TreeEntry:
	var t := TreeEntry.new()
	t.position = pos
	t.kind = kind
	t.seed_value = _rng.randi()
	t.scale = scale
	t.rotation = _rng.randf() * TAU
	t.far = far
	trees.append(t)
	return t


func _place_specimens() -> void:
	_add_tree(Vector2(17.5, -11.5), TreeSpecies.Kind.OAK, 1.18)
	_add_tree(Vector2(-13.0, -19.0), TreeSpecies.Kind.PINE, 1.1)
	_add_tree(Vector2(21.0, 13.5), TreeSpecies.Kind.ALDER, 1.05)
	_add_tree(Vector2(-20.0, 23.0), TreeSpecies.Kind.OAK, 1.0)
	_add_tree(Vector2(10.0, -26.0), TreeSpecies.Kind.SPRUCE, 1.05)
	_add_tree(Vector2(-9.0, -31.0), TreeSpecies.Kind.SNAG, 1.0)
	_add_tree(Vector2(29.0, 22.0), TreeSpecies.Kind.SNAG, 0.9)


func _blocked(pos: Vector2, spacing: float) -> bool:
	if _shore_offset(pos) < 3.5:
		return true
	if _field.trail_distance(pos.x, pos.y) < 3.6:
		return true
	if pos.distance_to(TerrainField.FIRE) < 9.0 or pos.distance_to(TerrainField.TENT) < 6.0:
		return true
	if _field.is_underwater(pos.x, pos.y):
		return true
	# Preserve the authored crane into the clearing, including the wider
	# crowns of mature trees along its high opening section.
	if TerrainField.distance_to_polyline(pos, _intro_lane) < 8.5:
		return true
	for t in trees:
		if t.position.distance_to(pos) < spacing:
			return true
	return false


## Signed distance from the shoreline: negative inside the pond.
static func _shore_offset(pos: Vector2) -> float:
	var to_centre := pos - TerrainField.POND_CENTRE
	return to_centre.length() - TerrainField.pond_radius_at(atan2(to_centre.y, to_centre.x))


## Species mix depends on where the tree stands: conifers dominate the higher
## north and east ground, broadleaf the south and the low western shore.
## Species by place. The bank line and the wood behind the camp read as a
## wall of rounded crowns from the arrival and pond views when every tree
## there is broadleaf, so a share of dark spruce spires is mixed in everywhere
## and the conifer belt to the north-east stays denser.
func _species_for(pos: Vector2) -> TreeSpecies.Kind:
	var shore := _shore_offset(pos)
	if shore < 12.0:
		var bank := _rng.randf()
		if bank < 0.13:
			return TreeSpecies.Kind.SPRUCE
		return TreeSpecies.Kind.ALDER if bank < 0.74 else TreeSpecies.Kind.OAK
	var angle := atan2(pos.x, -pos.y)
	var north_east := clampf(cos(angle - PI * 0.25) * 0.5 + 0.5, 0.0, 1.0)
	var roll := _rng.randf()
	var conifer_chance := lerpf(0.16, 0.44, north_east)
	if shore < 25.0:
		conifer_chance *= 0.6
	if roll < conifer_chance:
		return TreeSpecies.Kind.SPRUCE if _rng.randf() < 0.62 else TreeSpecies.Kind.PINE
	return TreeSpecies.Kind.OAK if _rng.randf() < 0.55 else TreeSpecies.Kind.ALDER


## Most trees share a canopy height; a few emergents stand a third taller so
## the skyline is not a level hedge.
func _tree_scale(low: float, high: float) -> float:
	if _rng.randf() < 0.09:
		return _rng.randf_range(1.30, 1.50)
	return _rng.randf_range(low, high)


## Regenerating ground remains inside the western woodland. A separately
## composed bank canopy encloses its former broad entrance from the pond.
static func sunset_opening(pos: Vector2) -> float:
	return TerrainField.woodland_opening(pos)


func _density_bias(pos: Vector2) -> float:
	var patch := _grove_noise.get_noise_2d(pos.x, pos.y)
	return clampf(0.66 + patch * 0.95, 0.18, 1.0) * (1.0 - sunset_opening(pos))


func _woodland_position(inner: float, outer: float) -> Vector2:
	var a := _rng.randf() * TAU
	if _rng.randf() < 0.65:
		var grove := WOODLAND_GROVES[_rng.randi() % WOODLAND_GROVES.size()]
		var offset := Vector2(cos(a) * 1.12, sin(a) * 0.84) * sqrt(_rng.randf()) * grove.z
		return Vector2(grove.x, grove.y) + offset.rotated(grove.x * 0.027)
	var radius := sqrt(lerpf(inner * inner, outer * outer, _rng.randf()))
	return Vector2(cos(a), sin(a)) * radius


func _place_treeline() -> void:
	var attempts := 0
	var target := 156
	var placed := 0
	while placed < target and attempts < target * 40:
		attempts += 1
		var pos := _woodland_position(TerrainField.TREELINE_INNER, NEAR_LIMIT)
		if pos.length() < TerrainField.TREELINE_INNER or pos.length() > NEAR_LIMIT:
			continue
		if _rng.randf() > _density_bias(pos):
			continue
		var scale := _tree_scale(0.78, 1.23)
		if _blocked(pos, NEAR_TREE_MIN_SPACING * scale * _rng.randf_range(0.78, 1.18)):
			continue
		_add_tree(pos, _species_for(pos), scale)
		placed += 1


func _place_far_forest() -> void:
	var attempts := 0
	var target := 300
	var placed := 0
	while placed < target and attempts < target * 30:
		attempts += 1
		var pos := _woodland_position(NEAR_LIMIT, FAR_LIMIT)
		if pos.length() < NEAR_LIMIT or pos.length() > FAR_LIMIT:
			continue
		if _rng.randf() > _density_bias(pos) * 0.95:
			continue
		var scale := _tree_scale(0.78, 1.28)
		if _blocked(pos, FAR_TREE_MIN_SPACING * scale * _rng.randf_range(0.70, 1.20)):
			continue
		_add_tree(pos, _species_for(pos), scale, true)
		placed += 1


func _place_saplings() -> void:
	var attempts := 0
	var placed := 0
	while placed < 26 and attempts < 600:
		attempts += 1
		var angle := _rng.randf() * TAU
		var radius := _rng.randf_range(TerrainField.CLEARING_RADIUS - 2.0, TerrainField.TREELINE_INNER + 8.0)
		var pos := Vector2(cos(angle) * radius, sin(angle) * radius)
		if pos.distance_to(Vector2(7.8, 28.3)) < 3.5:
			continue # Keep the opening dolly's low pass through saplings clear.
		if _blocked(pos, 2.2):
			continue
		_add_tree(pos, TreeSpecies.Kind.SAPLING, _rng.randf_range(0.8, 1.2))
		placed += 1
	# Regeneration belongs to the woodland too, not only a ring around camp.
	# Taller juveniles sit among low saplings; a few openings remain legible.
	placed = 0
	attempts = 0
	while placed < 112 and attempts < 2400:
		attempts += 1
		var pos := _woodland_position(38.0, 150.0)
		if pos.length() < 38.0 or pos.length() > 150.0 or _blocked(pos, 1.7):
			continue
		if _rng.randf() > _density_bias(pos) + sunset_opening(pos) * 0.18:
			continue
		var juvenile := _rng.randf() < 0.20 and sunset_opening(pos) < 0.2
		var kind := _species_for(pos) if juvenile else TreeSpecies.Kind.SAPLING
		if juvenile and (kind == TreeSpecies.Kind.SPRUCE or kind == TreeSpecies.Kind.PINE):
			kind = TreeSpecies.Kind.OAK
		var scale := _rng.randf_range(0.40, 0.61) if juvenile else _rng.randf_range(0.8, 1.5)
		_add_tree(pos, kind, scale, pos.length() > NEAR_LIMIT)
		placed += 1


## Staggered wet-bank alders, spreading oaks behind, and younger growth below
## close the former sunset corridor without forming a single row of crowns.
## This pass runs after the original layout and canopy paint: its private RNG
## preserves the camp, existing trees, rocks, ferns, flowers and shore reeds.
## The enclosed bank retains its established grassy ground layer.
func _place_pond_enclosure() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = _grove_noise.seed + 67031
	# X/Z, scale and one of the six existing growth habits. These anchors
	# overlap in depth as well as screen space; displaced roots still respect
	# existing trees and boulders instead of moving the authored frame edges.
	var alders: Array[Vector4] = [
		Vector4(-59, -5, 1.04, 0), Vector4(-61, -14, 1.14, 4),
		Vector4(-65, 1, 1.03, 2), Vector4(-68, -20, 1.16, 1),
		Vector4(-70, -7, 0.96, 5), Vector4(-75, 7, 1.03, 0),
		Vector4(-82, -3, 0.89, 3), Vector4(-86, -16, 1.03, 4),
	]
	var oaks: Array[Vector4] = [
		Vector4(-68, -1, 1.12, 0), Vector4(-73, -16, 1.08, 3),
		Vector4(-76, -6, 1.28, 1), Vector4(-79, 11, 0.98, 4),
		Vector4(-81, -25, 1.10, 2), Vector4(-85, -11, 1.16, 5),
		Vector4(-89, 2, 1.23, 3), Vector4(-91, -23, 0.92, 0),
		Vector4(-98, -3, 1.10, 4), Vector4(-101, -16, 1.26, 1),
		Vector4(-107, 10, 1.08, 2), Vector4(-111, -30, 0.96, 0),
	]
	for group in [alders, oaks]:
		var kind := TreeSpecies.Kind.ALDER if group == alders else TreeSpecies.Kind.OAK
		for anchor: Vector4 in group:
			for attempt in 18:
				var pos := Vector2(anchor.x, anchor.y)
				if attempt > 0:
					var angle := rng.randf() * TAU
					var radius := sqrt(rng.randf()) * lerpf(2.0, 5.5, float(attempt) / 17.0)
					pos += Vector2(cos(angle), sin(angle)) * radius
				if _enclosure_blocked(pos, 3.2 + anchor.z * 1.2, 0.9):
					continue
				_add_enclosure_tree(pos, kind, anchor.z, int(anchor.w), rng)
				break
	# The lower canopy is uneven regeneration, including some taller young
	# alders among the small saplings, rather than an identical shrub hedge.
	var patches: Array[Vector3] = [Vector3(-61, -10, 7), Vector3(-70, 4, 8),
		Vector3(-74, -22, 8), Vector3(-88, -8, 11)]
	for patch_index in patches.size():
		var patch := patches[patch_index]
		var placed := 0
		for attempt in 100:
			if placed >= 8:
				break
			var angle := rng.randf() * TAU
			var radius := sqrt(rng.randf()) * patch.z
			var pos := Vector2(patch.x, patch.y) + Vector2(cos(angle), sin(angle) * 0.74) * radius
			if _enclosure_blocked(pos, 1.55, 0.4):
				continue
			var juvenile := placed % 4 == patch_index
			var kind := TreeSpecies.Kind.ALDER if juvenile else TreeSpecies.Kind.SAPLING
			var scale := rng.randf_range(0.43, 0.64) if juvenile else rng.randf_range(0.55, 1.24)
			_add_enclosure_tree(pos, kind, scale, rng.randi() % 6, rng)
			placed += 1


func _enclosure_blocked(pos: Vector2, spacing: float, trunk_clearance: float) -> bool:
	if pos.x > -56.0 or _blocked(pos, spacing):
		return true
	for rock in rocks:
		if pos.distance_to(rock.position) < rock.scale + trunk_clearance:
			return true
	return false


func _add_enclosure_tree(pos: Vector2, kind: TreeSpecies.Kind, scale: float,
		variant: int, rng: RandomNumberGenerator) -> void:
	var entry := TreeEntry.new()
	entry.position = pos
	entry.kind = kind
	var seed_value := rng.randi()
	entry.seed_value = seed_value - posmod(seed_value, 6) + variant
	entry.scale = scale
	entry.rotation = rng.randf() * TAU
	entry.far = pos.length() > NEAR_LIMIT
	trees.append(entry)


# ------------------------------------------------------------------- rocks

func _add_rock(pos: Vector2, scale: float, sink: float) -> void:
	var r := RockEntry.new()
	r.position = pos
	r.scale = scale
	r.rotation = _rng.randf() * TAU
	r.sink = sink
	r.variant = _rng.randi() % 6
	rocks.append(r)


func _place_rocks() -> void:
	# Anchoring boulders: a cluster on the north bank and a few in the clearing.
	var authored := [
		[Vector2(-9.5, -10.5), 1.6], [Vector2(-7.8, -12.6), 1.0], [Vector2(-11.4, -12.0), 0.8],
		[Vector2(12.5, 7.0), 1.3], [Vector2(-15.0, 16.5), 1.1], [Vector2(22.0, -3.5), 1.9],
		[Vector2(-24.5, -9.0), 1.5], [Vector2(6.5, 31.0), 1.2], [Vector2(-8.0, 25.0), 0.9],
		[Vector2(-38.0, 24.0), 2.2], [Vector2(-42.0, -14.0), 1.7], [Vector2(-40.5, -11.0), 1.0],
	]
	for entry in authored:
		_add_rock(entry[0], entry[1], 0.38)
	# Shore pebbles and stones.
	for i in 60:
		var angle := _rng.randf() * TAU
		var radius := TerrainField.pond_radius_at(angle) + _rng.randf_range(0.2, 4.0)
		var pos := TerrainField.POND_CENTRE + Vector2(cos(angle), sin(angle)) * radius
		if _field.trail_distance(pos.x, pos.y) < 1.5 or pos.distance_to(TerrainField.DOCK_START) < 3.0:
			continue
		_add_rock(pos, _rng.randf_range(0.14, 0.42), 0.45)
	# Scattered stones through the wood.
	var placed := 0
	var attempts := 0
	while placed < 55 and attempts < 1200:
		attempts += 1
		var angle := _rng.randf() * TAU
		var radius := _rng.randf_range(12.0, 85.0)
		var pos := Vector2(cos(angle) * radius, sin(angle) * radius)
		if _field.trail_distance(pos.x, pos.y) < 2.5 or _field.is_underwater(pos.x, pos.y):
			continue
		if _shore_offset(pos) < 2.0:
			continue
		if pos.distance_to(TerrainField.FIRE) < 7.0 or pos.distance_to(TerrainField.TENT) < 5.0:
			continue
		var near_tree := false
		for t in trees:
			if t.position.distance_to(pos) < 1.6:
				near_tree = true
				break
		if near_tree:
			continue
		_add_rock(pos, _rng.randf_range(0.25, 1.3), 0.42)
		placed += 1


# ------------------------------------------------------------------ shrubs

func _place_shrubs() -> void:
	var placed := 0
	var attempts := 0
	while placed < 70 and attempts < 2000:
		attempts += 1
		var angle := _rng.randf() * TAU
		var radius := _rng.randf_range(TerrainField.CLEARING_RADIUS - 4.0, 70.0)
		var pos := Vector2(cos(angle) * radius, sin(angle) * radius)
		if _blocked(pos, 1.4):
			continue
		var s := ShrubEntry.new()
		s.position = pos
		s.scale = _rng.randf_range(0.7, 1.5)
		s.rotation = _rng.randf() * TAU
		shrubs.append(s)
		placed += 1
	# Low woody cover softens the exposed middle-distance trunks. This
	# shares the existing shrub mesh/material and adds no per-frame work.
	placed = 0
	attempts = 0
	while placed < 110 and attempts < 1800:
		attempts += 1
		var pos := _woodland_position(38.0, 128.0)
		if pos.length() < 38.0 or pos.length() > 128.0 or _blocked(pos, 1.25):
			continue
		if _rng.randf() > _density_bias(pos):
			continue
		var shrub := ShrubEntry.new()
		shrub.position = pos
		shrub.scale = _rng.randf_range(1.0, 1.9)
		shrub.rotation = _rng.randf() * TAU
		shrubs.append(shrub)
		placed += 1


# ------------------------------------------------------------------ canopy

func _paint_canopy() -> void:
	_field.canopy = ScalarField.new(320, TerrainField.INNER_EXTENT, 0.0)
	for t in trees:
		if t.position.length() > TerrainField.INNER_EXTENT * 0.5 + 10.0:
			continue
		var spread := crown_footprint(t.kind) * t.scale
		_field.canopy.paint_disc(t.position.x, t.position.y, spread * 0.45, spread * 1.05, 1.0)


static func crown_footprint(kind: TreeSpecies.Kind) -> float:
	match kind:
		TreeSpecies.Kind.OAK:
			return 6.5
		TreeSpecies.Kind.ALDER:
			return 4.5
		TreeSpecies.Kind.SPRUCE:
			return 3.6
		TreeSpecies.Kind.PINE:
			return 5.0
		TreeSpecies.Kind.SNAG:
			return 1.5
		TreeSpecies.Kind.SAPLING:
			return 1.2
		_:
			assert(false, "Unhandled species %s" % kind)
			return 4.0


## Add successive woodland age layers without moving existing authored trees.
## They enter the same plan as original trees: canopy paint, collision, camera
## clearance, culling and wildlife all see them. Never a visual-only overlay.
func _place_dense_stands() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 226091
	var cells := {}
	const CELL := 5.0
	for tree in trees:
		var cell := Vector2i(floori(tree.position.x / CELL), floori(tree.position.y / CELL))
		if not cells.has(cell):
			cells[cell] = []
		cells[cell].append(tree.position)
	# Mature near stand, overlapping middle distance, then juvenile regeneration.
	var budgets := [150, 310, 165]
	var added := 0
	for layer in 3:
		var placed := 0
		for attempt in budgets[layer] * 65:
			if placed >= budgets[layer]:
				break
			var a := rng.randf() * TAU
			var inner := 50.0 if layer == 0 else (NEAR_LIMIT if layer == 1 else 40.0)
			var outer := NEAR_LIMIT if layer == 0 else (205.0 if layer == 1 else 155.0)
			var r := sqrt(lerpf(inner * inner, outer * outer, rng.randf()))
			var at := Vector2(cos(a), sin(a)) * r
			var patch := _grove_noise.get_noise_2d(at.x, at.y)
			if rng.randf() > smoothstep(-0.48, 0.25, patch) * 0.9:
				continue
			if _shore_offset(at) < 3.2 or _field.is_underwater(at.x, at.y):
				continue
			if _field.slope(at.x, at.y) > 0.70 or _field.walking_distance(at) < 3.8:
				continue
			if TerrainField.distance_to_polyline(at, _intro_lane) < 10.0:
				continue
			var cell := Vector2i(floori(at.x / CELL), floori(at.y / CELL))
			var spacing := 3.45 if layer < 2 else 1.95
			var clear := true
			for dz in range(-1, 2):
				for dx in range(-1, 2):
					for other: Vector2 in cells.get(cell + Vector2i(dx, dz), []):
						if at.distance_squared_to(other) < spacing * spacing:
							clear = false
			if not clear:
				continue
			for rock in rocks:
				if at.distance_to(rock.position) < rock.scale * 0.65 + 0.55:
					clear = false
					break
			if not clear:
				continue
			var kind := TreeSpecies.Kind.OAK
			if layer == 2:
				kind = TreeSpecies.Kind.SAPLING
			elif _shore_offset(at) < 22.0:
				kind = TreeSpecies.Kind.ALDER
			elif at.x - at.y > 35.0 and rng.randf() < 0.45:
				kind = TreeSpecies.Kind.SPRUCE if rng.randf() < 0.60 else TreeSpecies.Kind.PINE
			elif rng.randf() < 0.35:
				kind = TreeSpecies.Kind.ALDER
			var tree := TreeEntry.new()
			tree.position = at
			tree.kind = kind
			tree.scale = rng.randf_range(0.76, 1.18) if layer < 2 else rng.randf_range(0.65, 1.55)
			tree.rotation = rng.randf() * TAU
			tree.seed_value = rng.randi()
			tree.far = at.length() > NEAR_LIMIT
			trees.append(tree)
			if not cells.has(cell):
				cells[cell] = []
			cells[cell].append(at)
			placed += 1
			added += 1
	print("SHOWCASE_FOREST added=%d total=%d" % [added, trees.size()])
