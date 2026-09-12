class_name TreeGenerator
extends RefCounted

## Recursive parametric tree generator.
##
## Produces a bark mesh (tapered, parallel-transported tubes with cylindrical
## UVs and vertex AO at branch junctions) and a leaf mesh (crossed alpha cards
## carrying their pivot, a wind phase, crown-depth AO and a LOD importance in
## vertex attributes). Deterministic for a given species and seed.

class Result:
	var bark: ArrayMesh
	var leaves: ArrayMesh
	var height := 0.0
	var trunk_radius := 0.0
	var crown_center := Vector3.ZERO
	var crown_radius := 1.0
	var leaf_card_count := 0
	var branch_count := 0

class LeafPlacement:
	var position: Vector3
	var direction: Vector3
	var size: float
	var importance: float

const GOLDEN_ANGLE := 2.399963
const BARK_TILE := 1.2
const MIN_BRANCH_RADIUS := 0.008
const MIN_BRANCH_LENGTH := 0.22

var species: TreeSpecies
var rng := RandomNumberGenerator.new()
## 1.0 is the full tree. Lower values keep the same skeleton and crown but
## build the tubes with fewer sides and segments and emit only the most
## important leaf cards, grown to keep the canopy's coverage: the distant
## woodland uses the same species at a fraction of the triangles.
var detail := 1.0
var bark := MeshBuilder.new()
var leaves := MeshBuilder.new()
var height := 0.0
var _placements: Array[LeafPlacement] = []
var _branch_count := 0
var _oak_terminal_limbs: Array[Dictionary] = []


func generate(p_species: TreeSpecies, seed_value: int, p_detail := 1.0) -> Result:
	species = p_species
	detail = clampf(p_detail, 0.15, 1.0)
	rng.seed = seed_value
	bark = MeshBuilder.new()
	leaves = MeshBuilder.new()
	leaves.use_custom0 = true
	_placements.clear()
	_branch_count = 0
	_oak_terminal_limbs.clear()

	height = rng.randf_range(species.height.x, species.height.y)
	var result := Result.new()
	result.height = height
	result.trunk_radius = rng.randf_range(species.trunk_radius.x, species.trunk_radius.y)
	_grow_trunk(result.trunk_radius)
	_refine_oak_terminal_limbs(seed_value)
	_emit_leaves(result)
	result.bark = bark.commit(null, true)
	result.leaves = leaves.commit(null, false) if not leaves.is_empty() else null
	result.leaf_card_count = leaves.triangle_count() / 2
	result.branch_count = _branch_count
	return result


# --------------------------------------------------------------------- trunk

func _grow_trunk(base_radius: float) -> void:
	var split_at := clampf(species.split_height + rng.randf_range(-0.025, 0.025),
			species.branch_start + 0.06, 0.75) if species.split_height > 0.0 else 0.0
	var trunk_len := height * (split_at if split_at > 0.0 else 1.0)
	var lean := Vector3(rng.randf_range(-1.0, 1.0), 0.0, rng.randf_range(-1.0, 1.0)).normalized() * species.trunk_lean
	var dir := (Vector3.UP + lean).normalized()
	var points := _polyline(Vector3.ZERO, dir, trunk_len, species.trunk_segments, species.trunk_wobble, 0.15)
	var radii: Array[float] = []
	var n := points.size()
	for i in n:
		var t := float(i) / float(n - 1)
		var r := base_radius * pow(1.0 - species.trunk_taper * t * (split_at if split_at > 0.0 else 1.0), 0.95)
		# Basal flare: most of the widening happens in the bottom half metre,
		# so the trunk meets the ground as a spreading base rather than a pipe.
		if t < 0.1:
			var flare := 1.0 - t / 0.1
			r *= 1.0 + (species.root_flare - 1.0) * flare * flare * flare
		radii.append(maxf(r, 0.02))
	_add_bark_tube(points, radii, 0, 1.0)
	_branch_count += 1

	# Side branches along the trunk.
	var zone_start := species.branch_start * height
	var zone_end := minf(species.branch_end * height, trunk_len)
	if zone_end > zone_start:
		var count := int(round((zone_end - zone_start) * species.branch_density))
		var azimuth := rng.randf() * TAU
		for i in count:
			var h := zone_start + (float(i) + rng.randf_range(0.2, 0.8)) / float(count) * (zone_end - zone_start)
			var t := h / trunk_len
			var origin := _point_along(points, t)
			var tangent := _tangent_along(points, t)
			azimuth += GOLDEN_ANGLE + rng.randf_range(-0.3, 0.3)
			var angle := rng.randf_range(species.branch_angle.x, species.branch_angle.y)
			var child_dir := _rotate_away(tangent, azimuth, angle)
			var rel := h / height
			var length_scale := _branch_length_scale(rel)
			var length := rng.randf_range(species.branch_length.x, species.branch_length.y) * height * length_scale
			var parent_r := _radius_along(radii, t)
			var r := parent_r * species.branch_radius_ratio * (0.7 + 0.6 * length_scale)
			_grow_branch(origin, child_dir, length, r, 1, species.gravitropism)

	# Leaders where the trunk splits into a crown. Each leader is rooted a
	# little way down inside the trunk, so it emerges through the trunk's
	# capped top instead of hanging off its rim, and the fork reads as one
	# piece of wood.
	if split_at > 0.0:
		var leaders := rng.randi_range(species.split_count.x, species.split_count.y)
		var az := rng.randf() * TAU
		for i in leaders:
			# One dominant continuation covers the trunk cap. Other scaffold
			# limbs emerge lower and at unequal angles, avoiding a candelabra.
			var attachment := 1.0 if i == 0 else rng.randf_range(0.72, 0.92)
			var top := _point_along(points, attachment)
			var top_dir := _tangent_along(points, attachment)
			var r_top := _radius_along(radii, attachment)
			az += GOLDEN_ANGLE + rng.randf_range(-0.38, 0.38)
			var angle_scale := rng.randf_range(0.40, 0.70) if i == 0 else rng.randf_range(0.8, 1.15)
			var d := _rotate_away(top_dir, az, species.split_angle * angle_scale)
			var length := height * (1.0 - split_at) * rng.randf_range(0.80, 1.08)
			var r := r_top * (0.72 if i == 0 else rng.randf_range(0.50, 0.66))
			var root := top - top_dir * (r_top * 1.4) + _rotate_away(top_dir, az, PI * 0.5) * (r_top * 0.20)
			# Leaders leave along the trunk and curve outwards.
			_grow_branch(root, d, length + r_top * 1.4, r, 1, species.gravitropism, true, top_dir)


## Branches shorten towards the top of a cone (conifers) or keep a rounded
## crown (broadleaf) so the silhouette reads as the species.
func _branch_length_scale(rel_height: float) -> float:
	if species.conical:
		var t := (rel_height - species.branch_start) / maxf(species.branch_end - species.branch_start, 1e-3)
		return lerpf(1.0, 0.12, pow(clampf(t, 0.0, 1.0), 0.85))
	var t := (rel_height - species.branch_start) / maxf(species.branch_end - species.branch_start, 1e-3)
	return 0.55 + 0.45 * sin(clampf(t, 0.0, 1.0) * PI)


# ------------------------------------------------------------------ branches

func _grow_branch(origin: Vector3, dir: Vector3, length: float, radius: float, level: int,
		gravitropism: float, is_leader := false, from_dir := Vector3.ZERO) -> void:
	if radius < MIN_BRANCH_RADIUS or length < MIN_BRANCH_LENGTH or level > species.levels:
		return
	_branch_count += 1
	var segments := maxi(species.branch_segments - (level - 1), 2)
	if is_leader:
		# Thick limbs bend out of the trunk; enough rings keep the bend a curve.
		segments += 5
	var wobble := species.branch_wobble * (1.0 + 0.35 * float(level))
	var points := _polyline(origin, dir, length, segments, wobble, gravitropism, from_dir)
	var radii: Array[float] = []
	var n := points.size()
	var tip_ratio := 0.35 if is_leader else 0.2
	for i in n:
		var t := float(i) / float(n - 1)
		radii.append(maxf(radius * lerpf(1.0, tip_ratio, t), 0.004))
	var junction_ao := 0.55 if not is_leader else 0.8
	_add_bark_tube(points, radii, level, junction_ao)

	# Children.
	if level < species.levels:
		var count := rng.randi_range(species.child_count.x, species.child_count.y)
		if is_leader:
			count += 2
		var az := rng.randf() * TAU
		for i in count:
			var t := lerpf(0.3, 0.95, (float(i) + rng.randf_range(0.1, 0.9)) / float(count))
			var p := _point_along(points, t)
			var tangent := _tangent_along(points, t)
			az += GOLDEN_ANGLE + rng.randf_range(-0.4, 0.4)
			var angle := rng.randf_range(species.child_angle.x, species.child_angle.y)
			var child_dir := _rotate_away(tangent, az, angle)
			# Keep children from diving straight down.
			if child_dir.y < -0.35:
				child_dir = (child_dir + Vector3.UP * 0.5).normalized()
			var child_len := length * rng.randf_range(species.child_length_ratio.x, species.child_length_ratio.y)
			var child_r := _radius_along(radii, t) * species.child_radius_ratio
			var broadleaf := species.kind == TreeSpecies.Kind.OAK or species.kind == TreeSpecies.Kind.ALDER or species.kind == TreeSpecies.Kind.SAPLING
			_grow_branch(p, child_dir, child_len, child_r, level + 1, gravitropism * (0.88 if broadleaf else 1.15))

	# Leaves.
	if species.has_leaves() and level >= species.leaf_level_min:
		var tip_dir := _tangent_along(points, 1.0)
		var base_size := rng.randf_range(species.leaf_card_size.x, species.leaf_card_size.y)
		for i in species.leaf_cards_per_tip:
			var jitter := Vector3(rng.randf_range(-0.15, 0.15), rng.randf_range(-0.1, 0.15), rng.randf_range(-0.15, 0.15)) * base_size
			var tip := _point_along(points, rng.randf_range(0.83, 1.0))
			_place_leaf(tip + jitter, _jitter_dir(tip_dir, 0.35), base_size * rng.randf_range(0.9, 1.15), rng.randf_range(0.72, 1.0))
		var along_first := _placements.size()
		for i in species.leaves_along_branch:
			var t := lerpf(0.4, 0.92, (float(i) + rng.randf()) / float(species.leaves_along_branch))
			var p := _point_along(points, t)
			var d := _jitter_dir(_tangent_along(points, t), 0.7)
			_place_leaf(p, d, base_size * rng.randf_range(0.80, 1.05), rng.randf_range(0.35, 0.95))
		if species.kind == TreeSpecies.Kind.OAK and level == 3 and length >= 0.9:
			_oak_terminal_limbs.append({points = points, radii = radii,
				first = along_first, count = species.leaves_along_branch, length = length})


## Fine oak shoots widen the leaf-bearing lobes inside the established crown.
## Run after the complete scaffold with a separate RNG: adding a shoot must not
## change later branches, existing tip sprays, or their atlas/wind random values.
func _refine_oak_terminal_limbs(seed_value: int) -> void:
	if _oak_terminal_limbs.is_empty():
		return
	var detail_rng := RandomNumberGenerator.new()
	detail_rng.seed = seed_value ^ 0x5EED0A17
	var center := Vector3.ZERO
	for lp in _placements:
		center += lp.position
	center /= float(_placements.size())
	var crown_radius := 0.0
	var crown_bounds := _leaf_bounds(_placements[0], center)
	for lp in _placements:
		crown_radius = maxf(crown_radius, lp.position.distance_to(center))
		crown_bounds = crown_bounds.merge(_leaf_bounds(lp, center))
	crown_bounds = crown_bounds.grow(-0.03)
	var extra_leaves: Array[LeafPlacement] = []
	for limb: Dictionary in _oak_terminal_limbs:
		if limb.count < 3 or detail_rng.randf() > 0.78:
			continue
		var points: Array[Vector3] = limb.points
		var radii: Array[float] = limb.radii
		var shoot_count := mini(detail_rng.randi_range(2, 3), int(limb.count) - 1)
		var azimuth := detail_rng.randf() * TAU
		for shoot in shoot_count:
			# Reuse outer along-branch sprays; terminal tip cards remain intact.
			var slot := 1 + roundi(float(shoot) / float(shoot_count - 1) * float(limb.count - 2))
			var lp := _placements[int(limb.first) + slot]
			var root := lp.position
			if root.y < height * 0.30 or root.distance_to(center) > crown_radius * 0.86:
				continue
			var t := lerpf(0.4, 0.92, (float(slot) + 0.5) / float(limb.count))
			var tangent := _tangent_along(points, t)
			azimuth += GOLDEN_ANGLE + detail_rng.randf_range(-0.25, 0.25)
			var lateral := _rotate_away(tangent, azimuth, detail_rng.randf_range(0.95, 1.38))
			var inward := (center - root).normalized()
			var direction := (lateral + inward * 0.32 + Vector3.UP * 0.12).normalized()
			var shoot_length := clampf(float(limb.length) * detail_rng.randf_range(0.14, 0.22), 0.3, 0.7)
			var tip := root + direction * shoot_length
			# Fill spaces between the limbs without growing a new outer envelope.
			if (tip + direction * lp.size).distance_to(center) > crown_radius * 0.97:
				continue
			var old_direction := lp.direction
			lp.position = root.lerp(tip, 0.72)
			lp.direction = direction
			if not crown_bounds.encloses(_leaf_bounds(lp, center)):
				lp.position = root
				lp.direction = old_direction
				continue
			var radius := clampf(_radius_along(radii, t) * 0.32, 0.006, 0.018)
			bark.add_tube([root, tip], [radius, 0.003], 4, Color(0.9, 1.0, 0.0, 1.0),
				1.0, 1.0 / BARK_TILE, detail_rng.randf(), true)
			_branch_count += 1
			# A few smaller inner sprays add lobe depth; preserve the old array
			# ordering so untouched cards retain their atlas, hue and wind phase.
			if detail_rng.randf() < 0.62:
				var inner := LeafPlacement.new()
				inner.position = root.lerp(tip, 0.38)
				inner.direction = (tangent * 0.5 + direction * 0.3 + Vector3.UP * 0.2).normalized()
				inner.size = lp.size * detail_rng.randf_range(0.76, 0.9)
				inner.importance = detail_rng.randf_range(0.50, 0.78)
				if crown_bounds.encloses(_leaf_bounds(inner, center)):
					extra_leaves.append(inner)
	_placements.append_array(extra_leaves)


## Bounds of the same two crossed cards emitted below, without consuming RNG.
func _leaf_bounds(lp: LeafPlacement, center: Vector3) -> AABB:
	var up := (lp.direction - Vector3.UP * species.leaf_droop * 0.6).normalized()
	var outward := (lp.position - center) * Vector3(1.0, 0.5, 1.0)
	if outward.length_squared() < 1e-4:
		return AABB(lp.position - Vector3.ONE * lp.size, Vector3.ONE * lp.size * 2.0)
	var right := up.cross(outward.normalized())
	if right.length_squared() < 1e-4:
		right = up.cross(Vector3.RIGHT)
	right = right.normalized()
	var right2 := right.rotated(up, PI * 0.5)
	var spread := right.abs().max(right2.abs()) * lp.size * 0.425
	var end := lp.position + up * lp.size
	var lower := lp.position.min(end) - spread
	return AABB(lower, lp.position.max(end) + spread - lower)


func _place_leaf(position: Vector3, direction: Vector3, size: float, importance: float) -> void:
	var lp := LeafPlacement.new()
	lp.position = position
	lp.direction = direction
	lp.size = size
	lp.importance = importance
	_placements.append(lp)


# ------------------------------------------------------------- leaf emission

func _emit_leaves(result: Result) -> void:
	if _placements.is_empty():
		return
	var center := Vector3.ZERO
	for lp in _placements:
		center += lp.position
	center /= float(_placements.size())
	var radius := 0.0
	for lp in _placements:
		radius = maxf(radius, lp.position.distance_to(center))
	result.crown_center = center
	result.crown_radius = maxf(radius, 0.5)

	var placements := _placements
	if detail < 1.0:
		# Keep the most important cards (tips first) and grow the survivors so
		# the crown covers the same area with fewer, larger cards.
		# A stratified pick through the importance order keeps inner, shaded
		# cards as well as the tips, so the thinned crown still has a dark
		# interior between its lit outer sprays instead of becoming a bright
		# shell of oversized cards.
		var sorted := _placements.duplicate()
		sorted.sort_custom(func(a: LeafPlacement, b: LeafPlacement) -> bool: return a.importance > b.importance)
		var keep := clampf(detail, 0.2, 1.0)
		var count := clampi(roundi(float(sorted.size()) * keep), mini(8, sorted.size()), sorted.size())
		placements = []
		var step := float(sorted.size()) / float(count)
		for k in count:
			placements.append(sorted[mini(int(floor(float(k) * step)), sorted.size() - 1)])
		var grow := pow(1.0 / keep, 0.35)
		for lp in placements:
			lp.size *= grow
	for lp in placements:
		var depth := lp.position.distance_to(center) / result.crown_radius
		var ao := lerpf(0.5, 1.0, smoothstep(0.1, 0.95, depth))
		var phase := rng.randf()
		var hue := rng.randf()
		var cell := Vector2i(rng.randi() % 2, rng.randi() % 2)
		var uv_rect := Rect2(float(cell.x) * 0.5, float(cell.y) * 0.5, 0.5, 0.5)
		var outward := (lp.position - center)
		outward.y *= 0.5
		if outward.length_squared() < 1e-4:
			outward = Vector3(rng.randf_range(-1, 1), 0.2, rng.randf_range(-1, 1))
		outward = outward.normalized()
		# Cards grow along the twig, drooping under their own weight.
		var up := (lp.direction - Vector3.UP * species.leaf_droop * 0.6).normalized()
		var right := up.cross(outward)
		if right.length_squared() < 1e-4:
			right = up.cross(Vector3.RIGHT)
		right = right.normalized()
		var color := Color(ao, hue, lp.importance, 1.0)
		var custom := Color(lp.position.x, lp.position.y, lp.position.z, phase)
		var w := lp.size * 0.85
		var h := lp.size
		_emit_card(lp.position, right, up, w, h, color, custom, uv_rect)
		# Second card crossed at 90 degrees so the cluster has volume.
		var right2 := right.rotated(up, PI * 0.5)
		var cell2 := Vector2i(rng.randi() % 2, rng.randi() % 2)
		var uv_rect2 := Rect2(float(cell2.x) * 0.5, float(cell2.y) * 0.5, 0.5, 0.5)
		_emit_card(lp.position, right2, up, w, h, color, custom, uv_rect2)


func _emit_card(pivot: Vector3, right: Vector3, up: Vector3, w: float, h: float,
		color: Color, custom: Color, uv_rect: Rect2) -> void:
	var p0 := pivot - right * (w * 0.5)
	var p1 := pivot + right * (w * 0.5)
	var p2 := p1 + up * h
	var p3 := p0 + up * h
	leaves.add_quad(p0, p1, p2, p3, color, custom, uv_rect)


# ------------------------------------------------------------------- helpers

## Vertex colour: r = junction AO (dark where a branch leaves its parent),
## g = branch level / 4 (wind stiffness), b = unused.
func _add_bark_tube(points: Array[Vector3], radii: Array[float], level: int, junction_ao: float) -> void:
	var r0 := radii[0]
	var sides := 14 if r0 > 0.18 else (10 if r0 > 0.08 else (6 if r0 > 0.035 else 4))
	sides = maxi(3 if r0 <= 0.035 else 4, roundi(float(sides) * lerpf(0.4, 1.0, detail)))
	var u_repeats := maxf(1.0, round(TAU * r0 / BARK_TILE))
	var level_code := float(level) / 4.0
	var ring_color := func(i: int) -> Color:
		var ao := lerpf(junction_ao, 1.0, clampf(float(i) / 1.5, 0.0, 1.0))
		return Color(ao, level_code, 0.0, 1.0)
	bark.add_tube(points, radii, sides, Color(1.0, level_code, 0.0, 1.0),
			u_repeats, 1.0 / BARK_TILE, rng.randf(), true, ring_color)


## from_dir, when given, is the parent's direction: the limb leaves along it
## and bends towards dir over its first half, so a fork is a curve of wood
## rather than two straight cylinders meeting at an angle.
func _polyline(origin: Vector3, dir: Vector3, length: float, segments: int, wobble: float,
		gravitropism: float, from_dir := Vector3.ZERO) -> Array[Vector3]:
	var pts: Array[Vector3] = [origin]
	var target := dir.normalized()
	var d := target if from_dir == Vector3.ZERO else from_dir.normalized()
	segments = maxi(2, roundi(float(segments) * lerpf(0.5, 1.0, detail)))
	var seg := length / float(segments)
	for i in segments:
		var t := float(i + 1) / float(segments)
		if from_dir != Vector3.ZERO:
			var bend := smoothstep(0.0, 0.55, t)
			d = d.slerp(target, bend).normalized()
		var lift := gravitropism * 0.42 * (0.6 + 0.8 * t)
		# Conifer branches droop then curl up at the very tip.
		if gravitropism < 0.0 and i == segments - 1:
			lift = 0.35
		d = (d + Vector3.UP * lift + _random_unit() * wobble).normalized()
		pts.append(pts[pts.size() - 1] + d * seg)
	return pts


func _random_unit() -> Vector3:
	var v := Vector3(rng.randf_range(-1.0, 1.0), rng.randf_range(-1.0, 1.0), rng.randf_range(-1.0, 1.0))
	return v.normalized() if v.length_squared() > 1e-6 else Vector3.UP


func _jitter_dir(dir: Vector3, amount: float) -> Vector3:
	return (dir + _random_unit() * amount).normalized()


## Rotates `tangent` away from itself by `angle` towards a perpendicular
## direction chosen by `azimuth`.
static func _rotate_away(tangent: Vector3, azimuth: float, angle: float) -> Vector3:
	var side := tangent.cross(Vector3.UP)
	if side.length_squared() < 1e-6:
		side = tangent.cross(Vector3.RIGHT)
	side = side.normalized()
	var up := tangent.cross(side).normalized()
	var perp := side * cos(azimuth) + up * sin(azimuth)
	return (tangent * cos(angle) + perp * sin(angle)).normalized()


static func _point_along(points: Array[Vector3], t: float) -> Vector3:
	var scaled := clampf(t, 0.0, 1.0) * float(points.size() - 1)
	var i := clampi(int(floor(scaled)), 0, points.size() - 2)
	return points[i].lerp(points[i + 1], scaled - float(i))


static func _tangent_along(points: Array[Vector3], t: float) -> Vector3:
	var scaled := clampf(t, 0.0, 1.0) * float(points.size() - 1)
	var i := clampi(int(floor(scaled)), 0, points.size() - 2)
	return (points[i + 1] - points[i]).normalized()


static func _radius_along(radii: Array[float], t: float) -> float:
	var scaled := clampf(t, 0.0, 1.0) * float(radii.size() - 1)
	var i := clampi(int(floor(scaled)), 0, radii.size() - 2)
	return lerpf(radii[i], radii[i + 1], scaled - float(i))
