class_name TerrainField
extends RefCounted

## The authored landscape as pure functions of (x, z).
##
## Everything spatial in the scene (terrain mesh, collision, water, scatter,
## player grounding) queries this class, so it is deliberately deterministic and
## side-effect free. Coordinates: +X east, +Z south, -Z north. The camp fire is
## the origin; the pond lies to the west so the evening sun sets across it.
##
## `height()` is the authored landscape; `height_fast()` reads the fine grid.
## `surface_height()` follows the actual rendered triangles for distant plants,
## where sampling the analytic hills can put their roots above or below the mesh.

const INNER_EXTENT := 260.0
const OUTER_EXTENT := 1400.0

const FIRE := Vector2(0.0, 0.0)
const TENT := Vector2(7.5, -4.5)
const POND_CENTRE := Vector2(-34.0, 4.0)
## Nominal radius; the real shoreline wanders around it (see pond_radius_at).
const POND_RADIUS := 17.0
const POND_MAX_RADIUS := POND_RADIUS * 1.32
const WATER_LEVEL := -0.9
const DOCK_START := Vector2(-15.9, 6.3)
const TABLE := Vector2(9.2, 1.2)
const WOODPILE := Vector2(4.6, -2.2)
const WOODPILE_YAW := -0.35
## Tangential seats leave the southwest pond trail and east camp tracks open.
const SEATS: Array[Vector2] = [Vector2(2.1, 2.4), Vector2(-2.3, -1.8), Vector2(1.35, -2.6)]
const CLEARING_RADIUS := 26.0
const TREELINE_INNER := 28.0
const TREELINE_OUTER := 78.0

## Trail from the southern arrival point, past the fire, out to the dock.
const TRAIL: Array[Vector2] = [
	Vector2(4.0, 70.0), Vector2(3.5, 50.0), Vector2(2.0, 34.0), Vector2(1.5, 20.0),
	Vector2(1.0, 9.0), Vector2(-1.5, 2.5), Vector2(-6.0, -2.5), Vector2(-12.0, -0.5),
	Vector2(-14.5, 3.0), DOCK_START,
]
const CAMP_TRACK: Array[Vector2] = [
	Vector2(1.8, 0.9), Vector2(3.3, -1.6), Vector2(5.0, -2.9), Vector2(6.3, -3.8), TENT,
]
const TABLE_TRACK: Array[Vector2] = [Vector2(3.7, 0.5), Vector2(6.4, 1.3), TABLE]

## Anything this far from the trail's bounding box is untouched by it.
const TRAIL_BOUNDS := Rect2(-18.0, -5.0, 25.0, 78.0)
const TRAIL_INFLUENCE := 4.0

## Ground level the meadow sits on; dips are compressed so dry land never
## drops to the water level anywhere but in the pond basin.
const MEADOW_BASE := 0.45
const DIP_COMPRESSION := 0.25

var _rolling := FastNoiseLite.new()
var _medium := FastNoiseLite.new()
var _detail := FastNoiseLite.new()
var _ridges := FastNoiseLite.new()
var _variation := FastNoiseLite.new()
var _woodland := FastNoiseLite.new()

## Canopy coverage (0 open sky .. 1 dense canopy) painted by the tree planner.
var canopy := ScalarField.new(2, INNER_EXTENT, 0.0)
## Baked heights over the walkable area (see bake_height_grid).
var height_grid: ScalarField
var height_grid_half := 0.0
## Immutable after TerrainBuilder prepares it, before worker scatter begins.
var surface_axis := PackedFloat32Array()
var surface_heights := PackedFloat32Array()


func _init(seed_value := 20260902) -> void:
	_rolling.seed = seed_value
	_rolling.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_rolling.frequency = 0.011
	_rolling.fractal_type = FastNoiseLite.FRACTAL_FBM
	_rolling.fractal_octaves = 2

	_medium.seed = seed_value + 1
	_medium.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_medium.frequency = 0.045
	_medium.fractal_octaves = 2

	_detail.seed = seed_value + 2
	_detail.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_detail.frequency = 0.19
	_detail.fractal_octaves = 3

	_ridges.seed = seed_value + 3
	_ridges.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_ridges.frequency = 0.0032
	_ridges.fractal_type = FastNoiseLite.FRACTAL_RIDGED
	_ridges.fractal_octaves = 4

	_variation.seed = seed_value + 4
	_variation.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_variation.frequency = 0.03
	_variation.fractal_octaves = 2
	# Same broad stand field as the wooded ridges, beyond the painted canopy.
	_woodland.seed = 1927
	_woodland.frequency = 0.016


# --------------------------------------------------------------------- geometry

## Ground height in metres.
func height(x: float, z: float) -> float:
	var p := Vector2(x, z)
	var d := p.length()

	var rolling := _rolling.get_noise_2d(x, z) * 2.6
	var medium := _medium.get_noise_2d(x, z) * 0.75
	var detail := _detail.get_noise_2d(x, z) * 0.22
	var undulation := rolling + medium + detail
	var meadow := MEADOW_BASE + (undulation if undulation > 0.0 else undulation * DIP_COMPRESSION)
	# The compressed meadow gives way to the raw landscape beyond the treeline.
	var wild := smoothstep(70.0, 130.0, d)
	var h := lerpf(meadow, undulation, wild)

	# The camp sits on a low shelf; the ground climbs gently to the east and
	# north, and stays low to the west so the sunset is visible over the pond.
	var west := smoothstep(-10.0, -60.0, x)
	h += smoothstep(30.0, 95.0, d) * 5.5 * (1.0 - west * 0.8)

	# Distant hills close the horizon behind the treeline without hiding the sky.
	if d > 140.0:
		var far := smoothstep(140.0, 520.0, d)
		h += far * (18.0 + (_ridges.get_noise_2d(x, z) * 0.5 + 0.5) * 42.0)
	h += _hill(p, Vector2(210.0, -170.0), 150.0, 46.0)
	h += _hill(p, Vector2(-320.0, -260.0), 190.0, 60.0)
	h += _hill(p, Vector2(260.0, 300.0), 170.0, 38.0)

	# Pond: carved relative to the water level so the shoreline always lands
	# where the shape says, with a shelving beach on the outside.
	var to_centre := p - POND_CENTRE
	var pd := to_centre.length()
	if pd < POND_MAX_RADIUS * 1.6:
		var s := pd / pond_radius_at(atan2(to_centre.y, to_centre.x))
		var w := 1.0 - smoothstep(1.15, 1.55, s)
		if w > 0.0:
			var bed := WATER_LEVEL + _pond_profile(s) + detail * 1.4 * (1.0 - smoothstep(0.8, 1.0, s))
			h = lerpf(h, bed, w)

	# Trail: blend towards the smooth large-scale surface and sink slightly.
	if TRAIL_BOUNDS.grow(TRAIL_INFLUENCE).has_point(p):
		var trail_mask := 1.0 - smoothstep(1.2, 3.4, trail_distance(x, z))
		# The pond carve already replaced the original detail near its shore.
		# Subtracting that detail again used to move the eastern waterline and
		# cut a dry outside sample below water. Keep the zero crossing exact.
		trail_mask *= smoothstep(0.015, 0.32, h - WATER_LEVEL)
		if trail_mask > 0.0:
			var smooth_h := maxf(h - detail - 0.06, WATER_LEVEL + 0.015)
			h = lerpf(h, smooth_h, trail_mask * 0.85)

	# Level pads for the fire circle and the tent.
	if p.distance_to(FIRE) < 6.5:
		h = _flatten(h, p, FIRE, 2.5, 6.5, _pad_height(FIRE))
	if p.distance_to(TENT) < 4.5:
		h = _flatten(h, p, TENT, 2.2, 4.5, _pad_height(TENT))
	return h


## Height from the baked grid where it exists (bilinear), else the function.
func height_fast(x: float, z: float) -> float:
	if height_grid != null and absf(x) < height_grid_half and absf(z) < height_grid_half:
		return height_grid.sample(x, z)
	return height(x, z)


func bake_surface_grid(axis: PackedFloat32Array) -> void:
	surface_axis = axis
	var n := axis.size()
	surface_heights.resize(n * n)
	for iz in n:
		for ix in n:
			surface_heights[iz * n + ix] = height_fast(axis[ix], axis[iz])


## Barycentric interpolation follows TerrainBuilder's a-b-d / a-d-c diagonal.
## Bilinear interpolation is not the surface of a non-planar rendered quad.
func surface_height(x: float, z: float) -> float:
	if surface_axis.is_empty():
		return height_fast(x, z)
	var n := surface_axis.size()
	var ix := clampi(surface_axis.bsearch(x) - 1, 0, n - 2)
	var iz := clampi(surface_axis.bsearch(z) - 1, 0, n - 2)
	var tx := clampf((x - surface_axis[ix]) / (surface_axis[ix + 1] - surface_axis[ix]), 0.0, 1.0)
	var tz := clampf((z - surface_axis[iz]) / (surface_axis[iz + 1] - surface_axis[iz]), 0.0, 1.0)
	var a := surface_heights[iz * n + ix]
	var b := surface_heights[iz * n + ix + 1]
	var c := surface_heights[(iz + 1) * n + ix]
	var d := surface_heights[(iz + 1) * n + ix + 1]
	return a * (1.0 - tx) + b * (tx - tz) + d * tz if tx >= tz else a * (1.0 - tz) + c * (tz - tx) + d * tx


func surface_slope(x: float, z: float) -> float:
	var dx := surface_height(x - 1.0, z) - surface_height(x + 1.0, z)
	var dz := surface_height(x, z - 1.0) - surface_height(x, z + 1.0)
	return atan(sqrt(dx * dx + dz * dz) * 0.5)


## Bakes `height()` on a square grid of `spacing` metres covering ±`half`.
## The grid doubles as the collision heightfield, so it is uniform.
func bake_height_grid(half: float, spacing: float) -> void:
	var resolution := int(round(half * 2.0 / spacing)) + 1
	var grid := ScalarField.new(resolution, float(resolution - 1) * spacing, 0.0)
	var origin := -float(resolution - 1) * spacing * 0.5
	var data := grid.data
	for iz in resolution:
		var z := origin + float(iz) * spacing
		var row := iz * resolution
		for ix in resolution:
			data[row + ix] = height(origin + float(ix) * spacing, z)
	grid.data = data
	height_grid = grid
	height_grid_half = float(resolution - 1) * spacing * 0.5


func _pad_height(at: Vector2) -> float:
	var undulation := _rolling.get_noise_2d(at.x, at.y) * 2.6 + _medium.get_noise_2d(at.x, at.y) * 0.75
	return MEADOW_BASE + (undulation if undulation > 0.0 else undulation * DIP_COMPRESSION)


func _flatten(h: float, p: Vector2, centre: Vector2, inner: float, outer: float, target: float) -> float:
	var w := 1.0 - smoothstep(inner, outer, p.distance_to(centre))
	return lerpf(h, target, w)


func _hill(p: Vector2, centre: Vector2, radius: float, amplitude: float) -> float:
	var d := p.distance_to(centre) / radius
	return amplitude * exp(-d * d * 1.8)


## Shoreline distance from the pond centre in direction `angle` (radians,
## atan2(z, x)). Smaller towards the camp in the east, wider to the west.
static func pond_radius_at(angle: float) -> float:
	return POND_RADIUS * (1.0 - 0.16 * cos(angle) + 0.10 * sin(2.0 * angle + 0.7)
			+ 0.07 * sin(3.0 * angle + 2.1) + 0.05 * sin(5.0 * angle + 1.0))


## Point on the shoreline in direction `angle`.
static func shore_point(angle: float) -> Vector2:
	return POND_CENTRE + Vector2(cos(angle), sin(angle)) * pond_radius_at(angle)


## Bed height relative to the water level as a function of the normalised
## distance from the centre (1 = shoreline): a flat deep bottom, a steeper
## underwater bank, gentle shallows and a shelving beach above the waterline.
static func _pond_profile(s: float) -> float:
	if s < 0.55:
		return -3.4
	if s < 0.85:
		return lerpf(-3.4, -0.6, smoothstep(0.55, 0.85, s))
	if s < 1.0:
		return lerpf(-0.6, 0.0, (s - 0.85) / 0.15)
	if s < 1.15:
		return lerpf(0.0, 0.45, (s - 1.0) / 0.15)
	return 0.45


## Smooth surface normal from central differences.
func normal(x: float, z: float, step := 0.5) -> Vector3:
	var hl := height(x - step, z)
	var hr := height(x + step, z)
	var hd := height(x, z - step)
	var hu := height(x, z + step)
	return Vector3(hl - hr, 2.0 * step, hd - hu).normalized()


## Slope in radians.
func slope(x: float, z: float) -> float:
	return acos(clampf(normal(x, z).y, -1.0, 1.0))


func trail_distance(x: float, z: float) -> float:
	var p := Vector2(x, z)
	if not TRAIL_BOUNDS.grow(TRAIL_INFLUENCE).has_point(p):
		return TRAIL_INFLUENCE + 1.0
	return distance_to_polyline(p, TRAIL)


static func distance_to_polyline(p: Vector2, points: Array[Vector2]) -> float:
	var best := INF
	for i in range(points.size() - 1):
		var a := points[i]
		var b := points[i + 1]
		var ab := b - a
		var t := clampf((p - a).dot(ab) / maxf(ab.length_squared(), 1e-6), 0.0, 1.0)
		best = minf(best, p.distance_to(a + ab * t))
	return best


func pond_distance(x: float, z: float) -> float:
	return Vector2(x, z).distance_to(POND_CENTRE)


## Depth of water above the ground (0 on dry land).
func water_depth(x: float, z: float) -> float:
	return maxf(WATER_LEVEL - height_fast(x, z), 0.0)


func is_underwater(x: float, z: float) -> bool:
	return height_fast(x, z) < WATER_LEVEL


# ------------------------------------------------------------------- materials

## Bare, trodden ground around the fire circle and the tent.
static func camp_wear(p: Vector2) -> float:
	var fire := 1.0 - smoothstep(2.3, 4.2, p.distance_to(FIRE))
	var tent := 1.0 - smoothstep(2.0, 3.2, p.distance_to(TENT))
	var table := 1.0 - smoothstep(1.0, 1.8, p.distance_to(TABLE))
	var irregular := sin(p.x * 2.8 + sin(p.y * 3.1)) * sin(p.y * 1.9 + 1.7) * 0.14
	var wear := maxf(maxf(fire, tent), table)
	if p.distance_squared_to(WOODPILE) < 9.0:
		# Shared with the prop placement: the pile, loose splits and chopping
		# block occupy a worked patch, with a feathered edge into the meadow.
		var local := (p - WOODPILE).rotated(WOODPILE_YAW)
		var pile_radius := ((local - Vector2(0.12, 0.25)) / Vector2(1.04, 1.14)).length()
		var block_radius := ((local - Vector2(0.95, 0.5)) / Vector2(0.70, 0.78)).length()
		var wood_work := 1.0 - smoothstep(0.90, 1.40, minf(pile_radius, block_radius))
		wear = maxf(wear, wood_work)
	if p.distance_squared_to(FIRE) < 20.25:
		for seat in SEATS:
			var outward := (seat - FIRE).normalized()
			var tangent := Vector2(outward.y, -outward.x)
			var footwell := p - (seat - outward * 0.32)
			var along := 1.0 - smoothstep(0.76, 1.12, absf(footwell.dot(tangent)))
			var across := 1.0 - smoothstep(0.32, 0.65, absf(footwell.dot(outward)))
			wear = maxf(wear, along * across)
			# Keep the entire seat and its wind-swept margin clear. A footwell
			# alone leaves tall blades growing through the back of the log.
			var beneath := p - seat
			var seat_along := 1.0 - smoothstep(1.10, 1.32, absf(beneath.dot(tangent)))
			var seat_across := 1.0 - smoothstep(0.43, 0.68, absf(beneath.dot(outward)))
			wear = maxf(wear, seat_along * seat_across)
	return clampf(wear + irregular * wear * (1.0 - wear), 0.0, 1.0)


## Ground material weights: r = grass, g = leaf litter, b = mud, a = trail.
## `known_height` / `known_slope` let callers that already sampled the surface
## (mesh builders, scatterers) skip the expensive re-evaluation.
func material_mask(x: float, z: float, known_height := NAN, known_slope := NAN) -> Color:
	var h := height_fast(x, z) if is_nan(known_height) else known_height
	var s := slope(x, z) if is_nan(known_slope) else known_slope
	var cover := woodland_cover(x, z)
	var steep := smoothstep(0.42, 0.9, s)
	var wobble := _variation.get_noise_2d(x * 3.0, z * 3.0) * 0.35
	var p := Vector2(x, z)

	var trail := 1.0 - smoothstep(0.32 + wobble, 1.22 + wobble, walking_distance(p))
	trail = maxf(trail, camp_wear(p) * (0.85 + wobble))
	var mud := 1.0 - smoothstep(0.05, 0.55, h - WATER_LEVEL)
	var litter := clampf(cover * 1.2 + steep * 0.8 + wobble * 0.6, 0.0, 1.0)
	var grass := clampf((1.0 - litter) * (1.0 - steep), 0.0, 1.0)

	grass *= 1.0 - mud
	litter *= 1.0 - mud
	grass *= 1.0 - trail
	litter *= 1.0 - trail * 0.9
	return Color(grass, litter, mud, trail)


## Low-frequency colour variation for foliage (0 = cool/lush, 1 = warm/dry).
func dryness(x: float, z: float) -> float:
	var shore_damp := 1.0 - smoothstep(0.3, 2.0, height_fast(x, z) - WATER_LEVEL)
	var cover := woodland_cover(x, z)
	return clampf((_variation.get_noise_2d(x, z) * 0.7 + 0.42) * (1.0 - cover * 0.45 - shore_damp * 0.48), 0.0, 1.0)


## Open hills keep turf; litter belongs to actual woodland stands. The old
## distance-only litter ring and clamped canopy-map edge made a bare band.
func woodland_cover(x: float, z: float) -> float:
	var p := Vector2(x, z)
	var transition := smoothstep(95.0, 155.0, p.length())
	var painted := canopy.sample(x, z)
	if transition <= 0.0:
		return painted
	var habitat := _woodland.get_noise_2d(x, z)
	var distant := smoothstep(-0.25, 0.35, habitat) * 0.82
	var opening := woodland_opening(p) * (1.0 - smoothstep(245.0, 325.0, p.length()))
	distant *= 1.0 - opening * 0.60
	return lerpf(painted, distant, transition)


## Pure field equivalent of the ScenePlan sunset corridor; no scene/autoload
## dependency is allowed while a terrain/grass worker evaluates habitat.
static func woodland_opening(p: Vector2) -> float:
	var from_bank := p - Vector2(-15.5, 9.0)
	var axis := Vector2(-0.958, -0.287).normalized()
	var along := from_bank.dot(axis)
	var across := absf(from_bank.dot(Vector2(-axis.y, axis.x)))
	var half_width := 3.0 + maxf(along, 0.0) * 0.13
	return smoothstep(22.0, 38.0, along) * (1.0 - smoothstep(half_width, half_width + 9.0, across))


## Narrow desire lines link the places campers actually use. The route also
## drives ground cover and scatter clearance; it is never a floating decal.
func walking_distance(p: Vector2) -> float:
	var main := trail_distance(p.x, p.y)
	if p.x > -1.0 and p.x < 12.0 and p.y > -7.0 and p.y < 4.0:
		main = minf(main, distance_to_polyline(p, CAMP_TRACK) + 0.26)
		main = minf(main, distance_to_polyline(p, TABLE_TRACK) + 0.30)
	return main


## Coherent tussocks break up the meadow without random bare holes.
func meadow_height(x: float, z: float) -> float:
	var n := _detail.get_noise_2d(x * 0.8, z * 0.8)
	return lerpf(0.28, 0.63, smoothstep(-0.26, 0.32, n))


## Suitability for grass blades: open, gentle, dry ground away from the trail.
func grass_suitability(x: float, z: float, known_height := NAN, known_slope := NAN) -> float:
	var h := height_fast(x, z) if is_nan(known_height) else known_height
	var s := slope(x, z) if is_nan(known_slope) else known_slope
	var dry_land := smoothstep(0.03, 0.30, h - WATER_LEVEL)
	var trail_edge := smoothstep(0.34, 1.22, walking_distance(Vector2(x, z)))
	var pad_edge := 1.0 - smoothstep(0.1, 0.85, camp_wear(Vector2(x, z)))
	# The material mask already suppressed grass at the trail; multiplying it
	# twice left wide bald bands. Plant from habitat instead: shorter woodland
	# grass continues under trees, with only the walked route and pads bare.
	var habitat := lerpf(0.98, 0.48, woodland_cover(x, z))
	return habitat * dry_land * trail_edge * pad_edge * (1.0 - smoothstep(0.48, 0.95, s))
