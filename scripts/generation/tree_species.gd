class_name TreeSpecies
extends RefCounted

## Parameter set for the recursive tree generator. All lengths are metres,
## angles are radians. Ranges are [min, max] pairs sampled per tree.

enum Kind { OAK, ALDER, SPRUCE, PINE, SNAG, SAPLING }

var kind: Kind
var name: String
var bark_set: String
var leaf_atlas: String

var height := Vector2(12.0, 16.0)
var trunk_radius := Vector2(0.32, 0.45)
var trunk_segments := 7
var trunk_taper := 0.55
var trunk_wobble := 0.08
var trunk_lean := 0.06
var root_flare := 1.45

## Where along the trunk the first branches appear (fraction of height).
var branch_start := 0.35
var branch_end := 0.92
## Branches per metre of trunk in the branching zone.
var branch_density := 1.6
var branch_angle := Vector2(deg_to_rad(38.0), deg_to_rad(62.0))
var branch_length := Vector2(0.28, 0.42)
var branch_radius_ratio := 0.42
var branch_segments := 4
## Positive lifts branch tips towards the sky, negative droops them.
var gravitropism := 0.35
var branch_wobble := 0.12
## Number of recursion levels below the trunk (1 = branches only).
var levels := 3
var child_count := Vector2i(3, 5)
var child_angle := Vector2(deg_to_rad(30.0), deg_to_rad(55.0))
var child_length_ratio := Vector2(0.55, 0.75)
var child_radius_ratio := 0.55
## Trunk splits into several leaders at this height fraction (0 = no split).
var split_height := 0.0
var split_count := Vector2i(3, 4)
var split_angle := deg_to_rad(28.0)
## Conifer whorls: branches shrink towards the top to form a cone.
var conical := false
var crown_base := 0.0

## Leaf cards.
var leaf_card_size := Vector2(0.9, 1.4)
var leaf_cards_per_tip := 2
var leaves_along_branch := 0
var leaf_level_min := 2
var leaf_color_variation := 0.08
var leaf_tint := Color(1.0, 1.0, 1.0)
var leaf_droop := 0.35
var canopy_scale := 1.0


static func oak() -> TreeSpecies:
	var s := TreeSpecies.new()
	s.kind = Kind.OAK
	s.name = "oak"
	s.bark_set = "bark_oak"
	s.leaf_atlas = "leaves_oak"
	s.height = Vector2(12.5, 17.5)
	s.trunk_radius = Vector2(0.40, 0.58)
	# Mature broadleaf trunks and scaffold limbs are hero geometry. The six
	# cached variants amortize this extra roundness across every instance.
	s.trunk_segments = 9
	s.trunk_taper = 0.43
	s.trunk_wobble = 0.14
	s.trunk_lean = 0.09
	s.root_flare = 1.9
	s.split_height = 0.42
	s.split_count = Vector2i(2, 3)
	s.split_angle = deg_to_rad(34.0)
	s.branch_start = 0.25
	s.branch_end = 0.96
	s.branch_density = 1.12
	s.branch_angle = Vector2(deg_to_rad(38.0), deg_to_rad(72.0))
	s.branch_length = Vector2(0.31, 0.47)
	s.branch_radius_ratio = 0.48
	s.branch_segments = 5
	s.gravitropism = 0.15
	s.branch_wobble = 0.18
	s.levels = 3
	s.child_count = Vector2i(3, 5)
	s.child_angle = Vector2(deg_to_rad(26.0), deg_to_rad(60.0))
	s.child_length_ratio = Vector2(0.48, 0.74)
	s.child_radius_ratio = 0.58
	# Slightly smaller clusters with more branch coverage read as leaves on a
	# branching crown rather than a handful of large green cards.
	s.leaf_card_size = Vector2(0.70, 1.04)
	s.leaf_cards_per_tip = 2
	s.leaves_along_branch = 5
	s.leaf_level_min = 2
	s.leaf_tint = Color(0.98, 1.0, 0.92)
	s.leaf_color_variation = 0.13
	s.leaf_droop = 0.38
	return s


static func alder() -> TreeSpecies:
	var s := TreeSpecies.new()
	s.kind = Kind.ALDER
	s.name = "alder"
	s.bark_set = "bark_oak"
	s.leaf_atlas = "leaves_birch"
	s.height = Vector2(11.0, 15.5)
	s.trunk_radius = Vector2(0.22, 0.31)
	s.trunk_segments = 9
	s.trunk_taper = 0.70
	s.trunk_wobble = 0.065
	s.trunk_lean = 0.065
	s.root_flare = 1.55
	s.split_height = 0.0
	s.branch_start = 0.27
	s.branch_end = 0.98
	s.branch_density = 2.65
	s.branch_angle = Vector2(deg_to_rad(28.0), deg_to_rad(57.0))
	s.branch_length = Vector2(0.21, 0.34)
	s.branch_radius_ratio = 0.38
	s.branch_segments = 5
	s.gravitropism = 0.23
	s.branch_wobble = 0.12
	s.levels = 3
	s.child_count = Vector2i(3, 4)
	s.child_angle = Vector2(deg_to_rad(23.0), deg_to_rad(52.0))
	s.child_length_ratio = Vector2(0.48, 0.70)
	s.child_radius_ratio = 0.54
	s.leaf_card_size = Vector2(0.58, 0.90)
	s.leaf_cards_per_tip = 2
	s.leaves_along_branch = 5
	s.leaf_level_min = 2
	s.leaf_tint = Color(1.0, 1.0, 0.9)
	s.leaf_color_variation = 0.15
	s.leaf_droop = 0.53
	return s


static func spruce() -> TreeSpecies:
	var s := TreeSpecies.new()
	s.kind = Kind.SPRUCE
	s.name = "spruce"
	s.bark_set = "bark_pine"
	s.leaf_atlas = "needles_spruce"
	s.height = Vector2(17.5, 25.0)
	s.trunk_radius = Vector2(0.32, 0.45)
	s.trunk_segments = 11
	s.trunk_taper = 0.92
	s.trunk_wobble = 0.035
	s.trunk_lean = 0.025
	s.root_flare = 1.5
	s.conical = true
	s.branch_start = 0.12
	s.branch_end = 0.985
	s.branch_density = 4.45
	s.branch_angle = Vector2(deg_to_rad(77.0), deg_to_rad(98.0))
	s.branch_length = Vector2(0.20, 0.27)
	s.branch_radius_ratio = 0.35
	s.branch_segments = 4
	s.gravitropism = -0.30
	s.branch_wobble = 0.09
	s.levels = 2
	s.child_count = Vector2i(3, 5)
	s.child_angle = Vector2(deg_to_rad(34.0), deg_to_rad(62.0))
	s.child_length_ratio = Vector2(0.39, 0.56)
	s.child_radius_ratio = 0.50
	s.leaf_card_size = Vector2(0.68, 1.04)
	s.leaf_cards_per_tip = 2
	s.leaves_along_branch = 7
	s.leaf_level_min = 1
	s.leaf_tint = Color(0.95, 1.0, 0.95)
	s.leaf_color_variation = 0.07
	s.leaf_droop = 0.58
	return s


static func pine() -> TreeSpecies:
	var s := TreeSpecies.new()
	s.kind = Kind.PINE
	s.name = "pine"
	s.bark_set = "bark_pine"
	s.leaf_atlas = "needles_spruce"
	s.height = Vector2(19.0, 27.0)
	s.trunk_radius = Vector2(0.36, 0.50)
	s.trunk_segments = 11
	s.trunk_taper = 0.58
	s.trunk_wobble = 0.12
	s.trunk_lean = 0.10
	s.root_flare = 1.52
	s.branch_start = 0.42
	s.branch_end = 0.98
	s.branch_density = 2.35
	s.branch_angle = Vector2(deg_to_rad(43.0), deg_to_rad(82.0))
	s.branch_length = Vector2(0.26, 0.40)
	s.branch_radius_ratio = 0.47
	s.branch_segments = 5
	s.gravitropism = 0.36
	s.branch_wobble = 0.24
	s.levels = 3
	s.child_count = Vector2i(2, 4)
	s.child_angle = Vector2(deg_to_rad(28.0), deg_to_rad(62.0))
	s.child_length_ratio = Vector2(0.44, 0.70)
	s.child_radius_ratio = 0.55
	# Dense needle clusters hide the branch rods; sparse fans showed bare sticks.
	s.leaf_card_size = Vector2(0.74, 1.05)
	s.leaf_cards_per_tip = 5
	s.leaves_along_branch = 5
	s.leaf_level_min = 1
	s.leaf_tint = Color(0.92, 1.0, 0.9)
	s.leaf_color_variation = 0.08
	s.leaf_droop = 0.22
	return s


static func snag() -> TreeSpecies:
	var s := oak()
	s.kind = Kind.SNAG
	s.name = "snag"
	s.leaf_atlas = ""
	s.height = Vector2(8.0, 12.0)
	s.trunk_radius = Vector2(0.32, 0.46)
	s.trunk_segments = 9
	s.split_height = 0.55
	s.split_count = Vector2i(2, 3)
	s.branch_density = 0.72
	s.levels = 2
	s.child_count = Vector2i(1, 2)
	s.leaf_cards_per_tip = 0
	s.leaves_along_branch = 0
	return s


static func sapling() -> TreeSpecies:
	var s := alder()
	s.kind = Kind.SAPLING
	s.name = "sapling"
	s.height = Vector2(2.6, 4.2)
	s.trunk_radius = Vector2(0.04, 0.07)
	s.trunk_segments = 5
	s.root_flare = 1.22
	s.branch_start = 0.24
	s.branch_density = 3.0
	s.branch_length = Vector2(0.30, 0.45)
	s.branch_segments = 3
	s.levels = 2
	s.child_count = Vector2i(2, 3)
	s.leaf_card_size = Vector2(0.42, 0.64)
	s.leaves_along_branch = 3
	s.leaf_level_min = 1
	return s


static func by_kind(kind: Kind) -> TreeSpecies:
	match kind:
		Kind.OAK:
			return oak()
		Kind.ALDER:
			return alder()
		Kind.SPRUCE:
			return spruce()
		Kind.PINE:
			return pine()
		Kind.SNAG:
			return snag()
		Kind.SAPLING:
			return sapling()
		_:
			assert(false, "Unhandled species %s" % kind)
			return oak()


## Six growth habits, rather than six seeds of one perfectly upright tree.
## Each call returns a fresh parameter set; cached meshes and source species
## are never mutated by an individual instance.
static func variant(kind: Kind, index: int) -> TreeSpecies:
	var s := by_kind(kind)
	var v := posmod(index, 6)
	if kind == Kind.OAK:
		var forks := [0.31, 0.40, 0.51, 0.35, 0.46, 0.55]
		# Fork angles stay under 45 degrees to keep leaders from forming
		# rigid, widely splayed V-shaped silhouettes.
		var angles := [42.0, 36.0, 28.0, 44.0, 38.0, 31.0]
		var spreads := [1.18, 0.98, 0.84, 1.12, 1.03, 0.89]
		s.split_height = forks[v]
		s.branch_start = forks[v] - [0.10, 0.15, 0.19, 0.11, 0.16, 0.20][v]
		s.split_angle = deg_to_rad(angles[v])
		s.branch_length *= spreads[v]
		s.gravitropism = [0.08, 0.16, 0.25, 0.10, 0.15, 0.22][v]
		s.trunk_lean = [0.15, 0.055, 0.025, 0.11, 0.075, 0.040][v]
		s.trunk_wobble *= [1.18, 0.86, 0.72, 1.08, 0.94, 0.80][v]
		s.height *= [0.90, 1.0, 1.10, 0.95, 1.05, 1.12][v]
	elif kind == Kind.ALDER or kind == Kind.SAPLING:
		s.branch_start = [0.20, 0.34, 0.27, 0.40, 0.23, 0.31][v]
		s.branch_length *= [1.16, 0.90, 1.08, 0.82, 1.12, 0.95][v]
		s.gravitropism = [0.17, 0.31, 0.22, 0.33, 0.18, 0.27][v]
		s.trunk_lean = [0.09, 0.03, 0.11, 0.025, 0.07, 0.045][v]
		s.branch_wobble *= [1.15, 0.80, 1.05, 0.74, 1.12, 0.90][v]
		s.height *= [0.88, 1.07, 0.95, 1.12, 0.92, 1.03][v]
	else:
		# Conifers vary in live-crown depth, branch reach and wind-shaped lean;
		# this keeps species identity while preventing repeated identical cones.
		s.branch_length *= [0.88, 1.12, 0.96, 1.06, 0.84, 1.02][v]
		s.branch_start *= [0.90, 1.08, 0.98, 1.13, 0.86, 1.03][v]
		s.trunk_lean *= [1.30, 0.65, 1.0, 1.45, 0.55, 0.90][v]
		s.height *= [1.05, 0.94, 1.0, 1.10, 0.90, 1.02][v]
	return s


func has_leaves() -> bool:
	return leaf_atlas != "" and (leaf_cards_per_tip > 0 or leaves_along_branch > 0)
