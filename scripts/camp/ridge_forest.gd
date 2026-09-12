class_name RidgeForest
extends Node3D

## A continuation of the camp's temperate woodland on the surrounding hills:
## the same generated oak, alder, spruce and pine models the near forest
## uses, planted to a closed canopy out to 655 m. There are no simplified
## crowns or billboards out there; the forest thins leaf cards only once
## they are smaller than a pixel and the bark carries mesh LODs.
const CELL := 160.0
const INNER := 140.0
const OUTER := 655.0
const TARGET := 30000
## The same species and generator as the near woodland, built at a lower
## detail level per distance band: fewer tube sides and the most important
## leaf cards grown to keep coverage. No billboards, no simplified crowns.
const BANDS := [
	{limit = 300.0, detail = 0.50, seed = 12000, spacing = 1.0},
	{limit = 700.0, detail = 0.25, seed = 13000, spacing = 1.22},
]
const VARIANTS := 6
var tree_count := 0
var species_counts: Dictionary = {}
var field: TerrainField
var forest: Forest
var _variants: Dictionary = {}
var _detail_scale := 1.0
var _far_band := true

func _init(p_field: TerrainField, p_forest: Forest) -> void:
	field = p_field
	forest = p_forest
	name = "WoodedRidges"


func build() -> void:
	var preset: QualityPreset = Quality.current
	_detail_scale = preset.ridge_detail if preset != null else 1.0
	_far_band = preset.ridge_far_band if preset != null else true
	match Game.arg_value("ridge", ""):
		"full":
			_detail_scale = 1.0
			_far_band = true
		"light":
			_detail_scale = 0.5
			_far_band = true
		"near":
			_detail_scale = 0.5
			_far_band = false
	var rng := RandomNumberGenerator.new()
	rng.seed = 90265
	var groves := FastNoiseLite.new()
	groves.seed = 1927
	groves.frequency = 0.016
	var frontier_noise := FastNoiseLite.new()
	frontier_noise.seed = 3801
	frontier_noise.frequency = 0.008
	var age_noise := FastNoiseLite.new()
	age_noise.seed = 824
	age_noise.frequency = 0.010
	var groups := {}
	var occupied := {}
	const SPACE_CELL := 3.0
	# Random disc sampling with local spacing avoids visible planting rows
	# on distant slopes and in elevated camera views.
	for attempt in TARGET * 6:
		if tree_count >= TARGET:
			break
		var angle := rng.randf() * TAU
		var radius := sqrt(lerpf(INNER * INNER, OUTER * OUTER, rng.randf()))
		var p := Vector2(cos(angle), sin(angle)) * radius
		var western := smoothstep(0.45, 0.90, -p.x / radius)
		# The sunset opening formerly exposed a circular wall of mature ridge
		# trees exactly at 180 m. Stagger that frontier into overlapping stands.
		var frontier := lerpf(180.0, 163.0 + frontier_noise.get_noise_2d(p.x, p.y) * 48.0, western)
		var arrival := smoothstep(frontier, frontier + lerpf(0.1, 72.0, western), radius)
		var habitat := groves.get_noise_2d(p.x, p.y)
		# Closed canopy almost everywhere: the habitat noise only opens the
		# odd glade, and the regenerating west stays thinner but never bare.
		var density := lerpf(lerpf(0.62, 1.0, smoothstep(-0.32, 0.30, habitat)),
			lerpf(0.18, 1.0, smoothstep(-0.28, 0.28, habitat)), western)
		# Keep low regeneration in the front of the sun gap, with mature
		# woodland beyond it. This is not an empty wedge to the horizon.
		var opening := ScenePlan.sunset_opening(p) * (1.0 - smoothstep(245.0, 325.0, radius))
		if rng.randf() > density * arrival * (1.0 - opening * 0.40):
			continue
		var age := clampf(0.50 + age_noise.get_noise_2d(p.x, p.y) * 1.30 + rng.randf_range(-0.10, 0.10), 0.0, 1.0)
		age = lerpf(age, 0.10, opening * 0.95)
		var young := western > 0.45 and age < 0.34
		var middle := western > 0.45 and age >= 0.34 and age < 0.64
		var spacing := 2.6 if young else (4.0 if middle else 5.4)
		spacing = lerpf(5.0, spacing * rng.randf_range(0.86, 1.12), western)
		# Crowns still touch in the far band; its trees are simply larger apart
		# than the eye can separate at that range.
		if radius > float(BANDS[0].limit):
			spacing *= float(BANDS[1].spacing)
		var cell := Vector2i(floori(p.x / SPACE_CELL), floori(p.y / SPACE_CELL))
		var crowded := false
		# Three cells cover the largest pair spacing, including mixed ages.
		for z in range(-3, 4):
			for x in range(-3, 4):
				for other: Vector3 in occupied.get(cell + Vector2i(x, z), []):
					var gap := (spacing + other.z) * 0.5
					if p.distance_squared_to(Vector2(other.x, other.y)) < gap * gap:
						crowded = true
		if crowded:
			continue
		if not occupied.has(cell):
			occupied[cell] = []
		occupied[cell].append(Vector3(p.x, p.y, spacing))
		var y := field.surface_height(p.x, p.y)
		# The same mix as the woodland around the camp: a third conifers in
		# stands that follow the habitat noise, the rest oak-led broadleaf.
		# Alder carries the palest leaf atlas, so it stays a minority here.
		var conifer := not young and rng.randf() < lerpf(0.18, 0.48, smoothstep(-0.25, 0.30, habitat))
		var kind: TreeSpecies.Kind
		var scale_value: float
		if young:
			kind = TreeSpecies.Kind.ALDER if rng.randf() < 0.55 else TreeSpecies.Kind.OAK
			scale_value = rng.randf_range(0.50, 0.72)
		elif middle:
			kind = TreeSpecies.Kind.OAK if rng.randf() < 0.62 else TreeSpecies.Kind.ALDER
			scale_value = rng.randf_range(0.70, 0.90)
		elif conifer:
			kind = TreeSpecies.Kind.SPRUCE if rng.randf() < 0.65 else TreeSpecies.Kind.PINE
			scale_value = rng.randf_range(0.80, 1.10)
		else:
			kind = TreeSpecies.Kind.OAK if rng.randf() < 0.7 else TreeSpecies.Kind.ALDER
			scale_value = rng.randf_range(0.92, 1.28)
		var variant := rng.randi() % VARIANTS
		var band := 0
		while band < BANDS.size() - 1 and radius > float(BANDS[band].limit):
			band += 1
		if band > 0 and not _far_band:
			continue
		var key := "%d_%d_%d_%d_%d" % [band, int(kind), variant, floori(p.x / CELL), floori(p.y / CELL)]
		if not groups.has(key):
			groups[key] = {band = band, kind = kind, variant = variant, transforms = [] as Array[Transform3D]}
		var basis := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(scale_value, scale_value * rng.randf_range(0.92, 1.12), scale_value))
		groups[key].transforms.append(Transform3D(basis, Vector3(p.x, y - 0.12 * scale_value, p.y)))
		species_counts[kind] = species_counts.get(kind, 0) + 1
		tree_count += 1
	for key in groups:
		var group: Dictionary = groups[key]
		var variant_key := "%d_%d_%d" % [group.band, int(group.kind), group.variant]
		forest.add_ridge_group(group.kind, _variant(group.band, group.kind, group.variant), variant_key, group.transforms)
	var summary := PackedStringArray()
	for kind in species_counts:
		summary.append("%s=%d" % [TreeSpecies.Kind.keys()[kind].to_lower(), species_counts[kind]])
	print("Wooded ridges: %d trees out to %.0f m (%s) in %d batches" % [tree_count, OUTER, " ".join(summary), groups.size()])


func _variant(band: int, kind: TreeSpecies.Kind, index: int) -> TreeGenerator.Result:
	var key := "%d_%d_%d" % [band, int(kind), index]
	if not _variants.has(key):
		var spec: Dictionary = BANDS[band]
		var gen := TreeGenerator.new()
		_variants[key] = gen.generate(TreeSpecies.variant(kind, index), int(spec.seed) + int(kind) * 100 + index * 17, float(spec.detail) * _detail_scale)
	return _variants[key]
