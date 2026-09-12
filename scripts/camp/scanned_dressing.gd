class_name ScannedDressing
extends Node3D

## Photoscanned CC0 models (see models/SOURCES.md) placed as deterministic
## MultiMesh dressing beside the generated geometry: stumps, fallen trunks,
## surface roots at trunk bases, mossy rocks and boulders, small stones, ferns,
## shrubs, nettles and weeds, grass tufts, dry branches and conifer saplings.
## Placement follows the same route, camp and water rules as the generated
## dressing, keeps large pieces clear of every film and intro camera path, and
## batches per 32 m cell with distance culling. Small plants cast sun shadows
## only from cells near the camera (the same instances exist as a casting and a
## non-casting cell that hand over at SHADOW_RANGE).

const CELL := 32.0
const SEED := 51037
const SHADOW_RANGE := 45.0

## kind: rock, wood, plant, sapling. fade: metres at foliage_distance 1.
## lod0_only keeps the scan's LOD0 variants when a file carries several LODs.
const LIBRARY: Dictionary = {
	"tree_stump_01": {kind = "wood", fade = 170.0, collide = "cylinder"},
	"tree_stump_02": {kind = "wood", fade = 170.0, collide = "cylinder"},
	"dead_tree_trunk": {kind = "wood", fade = 190.0, collide = "capsule"},
	"dead_tree_trunk_02": {kind = "wood", fade = 190.0, collide = "capsule"},
	"dry_branches_medium_01": {kind = "wood", fade = 80.0},
	# Bark-toned: the darker tints made the clusters read as dark mounds.
	"root_cluster_01": {kind = "wood", fade = 120.0, tint = Color(0.64, 0.58, 0.50)},
	"root_cluster_02": {kind = "wood", fade = 110.0, tint = Color(0.64, 0.58, 0.50)},
	"single_root": {kind = "wood", fade = 110.0, tint = Color(0.66, 0.60, 0.52)},
	"rock_moss_set_01": {kind = "rock", fade = 200.0, collide = "sphere"},
	"rock_moss_set_02": {kind = "rock", fade = 200.0, collide = "sphere"},
	"boulder_01": {kind = "rock", fade = 240.0, collide = "sphere"},
	"rock_07": {kind = "rock", fade = 70.0},
	"rock_09": {kind = "rock", fade = 60.0},
	"stone_01": {kind = "rock", fade = 70.0},
	"fern_02": {kind = "plant", fade = 95.0, tint = Color(0.66, 0.72, 0.58)},
	"shrub_01": {kind = "plant", fade = 110.0, tint = Color(0.56, 0.62, 0.50)},
	"shrub_02": {kind = "plant", fade = 130.0, tint = Color(0.52, 0.60, 0.48)},
	"shrub_03": {kind = "plant", fade = 100.0, tint = Color(0.56, 0.62, 0.50)},
	"shrub_04": {kind = "plant", fade = 90.0, tint = Color(0.52, 0.60, 0.46)},
	"nettle_plant": {kind = "plant", fade = 85.0, tint = Color(0.58, 0.64, 0.52)},
	"weed_plant_02": {kind = "plant", fade = 75.0, tint = Color(0.60, 0.66, 0.54)},
	"grass_medium_01": {kind = "plant", fade = 60.0, tint = Color(0.70, 0.74, 0.58)},
	"grass_medium_02": {kind = "plant", fade = 60.0, tint = Color(0.70, 0.74, 0.58)},
	"fir_sapling": {kind = "sapling", fade = 160.0},
	"pine_sapling_small": {kind = "sapling", fade = 160.0},
	# camp props: whole assemblies placed once at authored spots
	"hatchet": {kind = "prop", fade = 60.0},
	"wooden_bucket_01": {kind = "prop", fade = 90.0, collide = "box"},
	"wooden_crate_01": {kind = "prop", fade = 110.0, collide = "box"},
	"wicker_basket_01": {kind = "prop", fade = 70.0},
	"pot_enamel_01": {kind = "prop", fade = 70.0},
	"brass_pot_01": {kind = "prop", fade = 70.0},
	"handsaw_wood": {kind = "prop", fade = 60.0},
	"modified_thermos": {kind = "prop", fade = 60.0},
	"wooden_lantern_01": {kind = "prop", fade = 90.0},
}

class ScanVariant:
	var model: String
	var mesh: Mesh
	var bounds: AABB
	var footprint: float

var counts: Dictionary = {}
var batches: Array[MultiMeshInstance3D] = []
var _camp: Camp
var _field: TerrainField
var _plan: ScenePlan
var _rng := RandomNumberGenerator.new()
var _variants: Dictionary = {}
var _prop_scenes: Dictionary = {}
var props: Array[Node3D] = []
var _placements: Dictionary = {}
var _occupied: Dictionary = {}
var _camera_samples: PackedVector3Array
var _camera_grid: Dictionary = {}
var _trunk_grid: Dictionary = {}
var _ranges: Array[Dictionary] = []
var _built := false


func setup(camp: Camp) -> void:
	_camp = camp
	setup_from(camp.field, camp.plan)


## Build from a field and plan alone (tests, tools); setup(camp) wraps this.
func setup_from(field: TerrainField, plan: ScenePlan) -> void:
	if _built:
		return
	_built = true
	name = "ScannedDressing"
	_field = field
	_plan = plan
	_rng.seed = SEED
	_camera_samples = camera_samples(_field)
	_index_obstacles()
	_load_library()
	_place_all()
	_upload()
	Quality.preset_changed.connect(_quality)
	_quality(Quality.current)
	var summary := PackedStringArray()
	for key: String in counts:
		summary.append("%s=%d" % [key, counts[key]])
	print("Scanned dressing: %s in %d batches" % [" ".join(summary), batches.size()])


# ------------------------------------------------------------------ library

func _load_library() -> void:
	# --scanned-id-colours paints each model a flat colour so a frame can be
	# traced back to the asset it shows.
	var id_colours := Game.has_flag("scanned-id-colours")
	var palette := [Color.RED, Color.BLUE, Color.MAGENTA, Color.CYAN, Color.YELLOW, Color.GREEN, Color.ORANGE, Color.PURPLE, Color.WHITE, Color.BLACK]
	var index := 0
	for model: String in LIBRARY:
		var scene: PackedScene = load("res://models/%s/%s.gltf" % [model, model])
		if scene == null:
			push_warning("Scanned model %s is missing; run tools/import_models.py" % model)
			continue
		if LIBRARY[model].kind == "prop":
			_prop_scenes[model] = scene
			index += 1
			continue
		var root := scene.instantiate()
		var list: Array[ScanVariant] = []
		for mi in _mesh_instances(root):
			if "_LOD" in mi.name and not mi.name.ends_with("_LOD0"):
				continue
			var v := ScanVariant.new()
			v.model = model
			v.mesh = mi.mesh
			v.bounds = mi.mesh.get_aabb()
			v.footprint = maxf(v.bounds.size.x, v.bounds.size.z) * 0.5
			var tint: Color = LIBRARY[model].get("tint", Color.WHITE)
			if id_colours:
				tint = palette[index % palette.size()]
				print("SCANNED_ID %s = %s" % [model, tint.to_html(false)])
			_tune_materials(v.mesh, LIBRARY[model].kind, tint, id_colours)
			list.append(v)
		index += 1
		root.free()
		list.sort_custom(func(a: ScanVariant, b: ScanVariant) -> bool: return a.footprint < b.footprint)
		_variants[model] = list


static func _mesh_instances(node: Node) -> Array[MeshInstance3D]:
	var out: Array[MeshInstance3D] = []
	var stack: Array[Node] = [node]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		if n is MeshInstance3D and (n as MeshInstance3D).mesh != null:
			out.append(n)
		for c in n.get_children():
			stack.append(c)
	out.sort_custom(func(a: MeshInstance3D, b: MeshInstance3D) -> bool: return a.name < b.name)
	return out


## The importer's StandardMaterial3D carries the scan's albedo, normal and ARM
## maps. Plants are cut out and lit from both sides; solids cull back faces.
static func _tune_materials(mesh: Mesh, kind: String, tint: Color, flat_colour := false) -> void:
	for s in mesh.get_surface_count():
		var mat := mesh.surface_get_material(s) as BaseMaterial3D
		if mat == null:
			continue
		mat.texture_filter = BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS_ANISOTROPIC
		# Scans are captured under flat light; a tint settles them into the
		# scene's darker, less saturated vegetation and soil.
		mat.albedo_color = tint
		if flat_colour:
			mat.albedo_texture = null
		if kind == "plant" or (kind == "sapling" and mat.transparency != BaseMaterial3D.TRANSPARENCY_DISABLED):
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
			mat.alpha_scissor_threshold = 0.42
			mat.cull_mode = BaseMaterial3D.CULL_DISABLED
			# Matte, with a faint backlight only: no sheen (it turned sunlit cards
			# white), but enough transmission that a weed between the lens and a
			# low sun is a green leaf rather than a black cut-out.
			mat.backlight_enabled = true
			mat.backlight = Color(0.16, 0.20, 0.12)
			mat.metallic_specular = 0.05
			mat.roughness_texture = null
			mat.roughness = 0.92
		else:
			mat.cull_mode = BaseMaterial3D.CULL_BACK
			mat.metallic_specular = 0.3


# ---------------------------------------------------------------- placement

func _place_all() -> void:
	_place_stumps()
	_place_trunks()
	# Scanned root clusters include large soil balls that overwhelm the camp
	# composition. Leave those optional assets unplaced and use generated
	# buttress roots to blend standing trees into the ground.
	_place_rocks()
	_place_stones()
	_place_plants("fern_02", 700, 10.0, 72.0, 0.42, 0.85, 1.2, Vector2(0.85, 1.45), 0.02)
	_place_plants("shrub_02", 70, 16.0, 86.0, 0.25, 3.2, 1.8, Vector2(0.75, 1.25), 0.04)
	_place_plants("shrub_03", 60, 14.0, 80.0, 0.30, 2.0, 1.4, Vector2(0.9, 1.4), 0.02)
	_place_plants("shrub_04", 40, 14.0, 80.0, 0.30, 1.6, 1.2, Vector2(1.0, 1.6), 0.02)
	_place_plants("shrub_01", 30, 16.0, 80.0, 0.35, 2.4, 1.4, Vector2(0.9, 1.3), 0.02)
	_place_plan_shrubs()
	_place_wet_plants("nettle_plant", 110, 3.0, 16.0, 1.1, Vector2(0.9, 1.5))
	_place_wet_plants("weed_plant_02", 140, 2.0, 22.0, 0.8, Vector2(1.2, 2.2))
	_place_tufts("grass_medium_01", 320, Vector2(1.3, 2.2))
	_place_tufts("grass_medium_02", 160, Vector2(1.1, 1.9))
	_place_plants("dry_branches_medium_01", 70, 14.0, 78.0, 0.35, 2.5, 1.5, Vector2(0.9, 1.4), 0.05, true)
	_place_saplings()
	_place_camp_props()


## Hero props at authored spots: a crate with a thermos on it and a lantern
## and pot by the tent door, a bucket and basket at the table, an enamel pot
## by the seat, and a saw and hatchet at the woodpile. Multi-part scans stay
## whole; each prop is one scene instance, not a MultiMesh.
func _place_camp_props() -> void:
	var tent := Cinematic.tent_transform(_field)
	var tent_yaw := tent.basis.get_euler().y
	var table := Vector2(TerrainField.TABLE.x, TerrainField.TABLE.y)
	var wood := TerrainField.WOODPILE
	var wood_side := Vector2(cos(TerrainField.WOODPILE_YAW), -sin(TerrainField.WOODPILE_YAW))
	var wood_front := Vector2(sin(TerrainField.WOODPILE_YAW), cos(TerrainField.WOODPILE_YAW))
	var crate_at := tent * Vector3(-1.55, 0.0, -1.25)
	_prop("wooden_crate_01", Vector2(crate_at.x, crate_at.z), tent_yaw + 0.35, 0.0)
	_prop("modified_thermos", Vector2(crate_at.x, crate_at.z) + Vector2(0.12, -0.05), tent_yaw + 1.2, 0.36)
	var lantern_at := tent * Vector3(1.05, 0.0, -1.65)
	_prop("wooden_lantern_01", Vector2(lantern_at.x, lantern_at.z), tent_yaw - 0.4, 0.0)
	var brass_at := tent * Vector3(1.3, 0.0, -1.1)
	_prop("brass_pot_01", Vector2(brass_at.x, brass_at.z), tent_yaw + 0.8, 0.0)
	_prop("wooden_bucket_01", table + Vector2(1.05, 0.45), 0.4, 0.0)
	_prop("wicker_basket_01", table + Vector2(0.55, -0.95), 1.2, 0.0)
	# Outer side of the third seat, away from the intro dolly's approach.
	_prop("pot_enamel_01", Vector2(1.8, -3.45), 0.9, 0.0)
	_prop("handsaw_wood", wood + wood_side * 1.15 + wood_front * 0.25, TerrainField.WOODPILE_YAW + 0.2, 0.0, Vector3(0.0, 0.0, PI * 0.5))
	_prop("hatchet", wood - wood_side * 1.0 - wood_front * 0.7, TerrainField.WOODPILE_YAW - 0.6, 0.0, Vector3(PI * 0.5, 0.0, 0.0))


func _prop(model: String, p: Vector2, yaw: float, lift: float, lay := Vector3.ZERO) -> void:
	var scene: PackedScene = _prop_scenes.get(model)
	if scene == null:
		return
	if not _camera_clear(p, 0.6):
		push_warning("Camp prop %s at %s sits on a camera path" % [model, p])
	var root := scene.instantiate() as Node3D
	var bounds := AABB()
	var first := true
	for mi in _mesh_instances(root):
		_tune_materials(mi.mesh, "prop", LIBRARY[model].get("tint", Color.WHITE))
		var part := mi.transform * mi.mesh.get_aabb()
		bounds = part if first else bounds.merge(part)
		first = false
	var ground := _field.height(p.x, p.y)
	var n := _field.normal(p.x, p.y)
	var basis := Basis(Vector3.UP, yaw)
	if lay != Vector3.ZERO:
		basis = basis * Basis.from_euler(lay)
	var up := Vector3.UP.lerp(n, 0.5).normalized()
	if up.distance_to(Vector3.UP) > 0.001:
		basis = Basis(Quaternion(Vector3.UP, up)) * basis
	# Rest the assembly's lowest point on the ground (or the surface it sits on).
	var rested := Transform3D(basis, Vector3.ZERO) * bounds
	root.transform = Transform3D(basis, Vector3(p.x, ground + lift - rested.position.y, p.y))
	root.name = "Prop_" + model
	if LIBRARY[model].has("collide"):
		var body := StaticBody3D.new()
		body.name = "PropCollision"
		body.collision_layer = 1
		body.collision_mask = 0
		body.set_meta("surface", &"wood")
		var shape := CollisionShape3D.new()
		var box := BoxShape3D.new()
		box.size = bounds.size
		shape.shape = box
		shape.position = bounds.get_center()
		body.add_child(shape)
		root.add_child(body)
	add_child(root)
	props.append(root)
	counts[model] = int(counts.get(model, 0)) + 1
	_placements["%s|prop" % model] = {variant = null, transforms = [root.transform]}


func _place_stumps() -> void:
	var models := ["tree_stump_01", "tree_stump_02"]
	var placed := 0
	var attempts := 0
	while placed < 14 and attempts < 3000:
		attempts += 1
		var p := _woodland_point(16.0, 75.0)
		if _field.canopy.sample(p.x, p.y) < 0.35 or not _allowed(p, 2.6, 0.2):
			continue
		if _trunk_distance(p) < 4.0 or not _spaced("stump", p, 9.0) or not _camera_clear(p, 2.4):
			continue
		var model: String = models[placed % models.size()]
		_add(model, p, _rng.randf_range(0.9, 1.25), 0.06, 0.55, true)
		placed += 1


func _place_trunks() -> void:
	var models := ["dead_tree_trunk", "dead_tree_trunk_02"]
	var placed := 0
	var attempts := 0
	while placed < 8 and attempts < 4000:
		attempts += 1
		var p := _woodland_point(18.0, 70.0)
		if _field.canopy.sample(p.x, p.y) < 0.3 or not _allowed(p, 3.2, 0.25):
			continue
		if _trunk_distance(p) < 3.0 or not _spaced("trunk", p, 12.0) or not _camera_clear(p, 3.6):
			continue
		var model: String = models[placed % models.size()]
		# Lay the trunk across the slope with a little random turn; the mesh
		# runs along its local X axis.
		var n := _field.normal(p.x, p.y)
		var downhill := Vector2(n.x, n.z)
		var yaw := _rng.randf() * TAU if downhill.length() < 0.03 else atan2(downhill.x, downhill.y) + PI * 0.5 + _rng.randf_range(-0.5, 0.5)
		_add(model, p, _rng.randf_range(0.95, 1.3), 0.09, 1.0, true, yaw)
		placed += 1


## Surface roots radiate from near trunks that are not part of the camp itself.
func _place_roots() -> void:
	# Mostly single roots, one per tree, tucked under the basal flare and
	# bedded into the soil: clusters standing a step from the trunk read as
	# dark mounds in the camp shots.
	var models := ["single_root", "single_root", "root_cluster_02", "root_cluster_01"]
	var placed := 0
	for t in _plan.near_trees():
		if t.position.length() > 80.0 or t.position.length() < 12.0:
			continue
		if _rng.randf() > 0.32:
			continue
		var species := TreeSpecies.by_kind(t.kind)
		if not species.has_leaves() and species.kind != TreeSpecies.Kind.SNAG:
			continue
		var trunk_r := species.trunk_radius.y * t.scale * species.root_flare
		var a := _rng.randf() * TAU
		var p := t.position + Vector2(cos(a), sin(a)) * (trunk_r * 0.3)
		if not _allowed(p, 1.6, 0.15) or not _camera_clear(p, 1.2):
			continue
		var model: String = models[_rng.randi() % models.size()]
		# Roots point away from the trunk; the scans run along local Z.
		var yaw := -a + PI * 0.5 + _rng.randf_range(-0.35, 0.35)
		_add(model, p, _rng.randf_range(0.7, 1.0), 0.16, 1.0, false, yaw)
		placed += 1


func _place_rocks() -> void:
	var placed := 0
	var attempts := 0
	while placed < 44 and attempts < 5000:
		attempts += 1
		var p := _woodland_point(13.0, 82.0)
		if not _allowed(p, 2.4, 0.1) or _trunk_distance(p) < 2.2:
			continue
		if not _spaced("rock", p, 6.5) or not _camera_clear(p, 2.2):
			continue
		var model := "rock_moss_set_01" if _rng.randf() < 0.5 else "rock_moss_set_02"
		_add(model, p, _rng.randf_range(0.7, 1.15), 0.16, 0.7, true)
		placed += 1
	placed = 0
	attempts = 0
	while placed < 6 and attempts < 4000:
		attempts += 1
		var p := _woodland_point(20.0, 70.0)
		if not _allowed(p, 3.5, 0.3) or _trunk_distance(p) < 3.5:
			continue
		if not _spaced("rock", p, 14.0) or not _camera_clear(p, 3.2):
			continue
		_add("boulder_01", p, _rng.randf_range(1.1, 1.7), 0.22, 0.6, true)
		placed += 1


## Small stones settle along the trail edges and the drier shore.
func _place_stones() -> void:
	var models := ["rock_07", "rock_09", "stone_01"]
	var placed := 0
	var attempts := 0
	while placed < 170 and attempts < 9000:
		attempts += 1
		var p: Vector2
		if _rng.randf() < 0.6:
			var trail: Array[Vector2] = TerrainField.TRAIL
			var seg := _rng.randi() % (trail.size() - 1)
			var along: Vector2 = trail[seg].lerp(trail[seg + 1], _rng.randf())
			var side := _rng.randf_range(1.3, 3.4) * (1.0 if _rng.randf() < 0.5 else -1.0)
			var dir: Vector2 = (trail[seg + 1] - trail[seg]).normalized()
			p = along + Vector2(-dir.y, dir.x) * side
		else:
			var a := _rng.randf() * TAU
			var shore := TerrainField.shore_point(a)
			p = shore + (shore - TerrainField.POND_CENTRE).normalized() * _rng.randf_range(0.6, 4.0)
		if not _allowed(p, 0.9, 0.05) or not _spaced("stone", p, 0.9):
			continue
		var model: String = models[_rng.randi() % models.size()] if _rng.randf() < 0.85 else "stone_01"
		_add(model, p, _rng.randf_range(1.2, 2.6), 0.25, 0.8, false)
		placed += 1


func _place_plants(model: String, target: int, inner: float, outer: float, min_cover: float,
		spacing: float, trail_clearance: float, scale: Vector2, sink: float, floor_align := false) -> void:
	var placed := 0
	var attempts := 0
	while placed < target and attempts < target * 14:
		attempts += 1
		var p := _woodland_point(inner, outer)
		if _field.canopy.sample(p.x, p.y) < min_cover:
			continue
		if not _allowed(p, trail_clearance, 0.12) or _trunk_distance(p) < 0.9:
			continue
		if not _spaced(model, p, spacing):
			continue
		# Bushes a step from the lens fill a film frame; keep them two metres off.
		if not _camera_clear(p, 2.0 if model.begins_with("shrub") else 1.0):
			continue
		_add(model, p, _rng.randf_range(scale.x, scale.y), sink, 1.0 if floor_align else 0.35, false)
		placed += 1


## The scene plan's shrubs around the camp used to be 21-card procedural
## bushes whose cards grew to half-metre single leaves beside the lens. Each
## planned bush is now one of the photoscanned shrubs at the same spot.
func _place_plan_shrubs() -> void:
	if _plan == null:
		return
	var models := ["shrub_02", "shrub_02", "shrub_03", "shrub_04", "shrub_01"]
	for shrub in _plan.shrubs:
		var p: Vector2 = shrub.position
		if not _camera_clear(p, 1.5):
			continue
		var model: String = models[_rng.randi() % models.size()]
		_add(model, p, clampf(shrub.scale * 0.85, 0.7, 1.5), 0.03, 0.35, false, shrub.rotation)


## Nettles and weeds follow the damp pond margin and the wetter clearing edge.
func _place_wet_plants(model: String, target: int, inner: float, outer: float, spacing: float, scale: Vector2) -> void:
	var placed := 0
	var attempts := 0
	while placed < target and attempts < target * 16:
		attempts += 1
		var a := _rng.randf() * TAU
		var shore := TerrainField.shore_point(a)
		var p := shore + (shore - TerrainField.POND_CENTRE).normalized() * _rng.randf_range(inner, outer)
		if not _allowed(p, 1.5, 0.12) or _trunk_distance(p) < 1.0 or not _spaced(model, p, spacing):
			continue
		if not _camera_clear(p, 1.0):
			continue
		_add(model, p, _rng.randf_range(scale.x, scale.y), 0.02, 0.3, false)
		placed += 1


## Scanned tufts sit where the camera passes: beside the trail and the camp.
func _place_tufts(model: String, target: int, scale: Vector2) -> void:
	var placed := 0
	var attempts := 0
	while placed < target and attempts < target * 14:
		attempts += 1
		var p: Vector2
		if _rng.randf() < 0.55:
			var trail: Array[Vector2] = TerrainField.TRAIL
			var seg := _rng.randi() % (trail.size() - 1)
			var along: Vector2 = trail[seg].lerp(trail[seg + 1], _rng.randf())
			var dir: Vector2 = (trail[seg + 1] - trail[seg]).normalized()
			p = along + Vector2(-dir.y, dir.x) * _rng.randf_range(0.9, 6.0) * (1.0 if _rng.randf() < 0.5 else -1.0)
		else:
			var a := _rng.randf() * TAU
			p = Vector2(cos(a), sin(a)) * sqrt(lerpf(6.0 * 6.0, 24.0 * 24.0, _rng.randf()))
		if not _allowed(p, 0.7, 0.1) or _trunk_distance(p) < 0.8 or not _spaced("tuft", p, 0.7):
			continue
		_add(model, p, _rng.randf_range(scale.x, scale.y), 0.015, 0.4, false)
		placed += 1


func _place_saplings() -> void:
	var models := ["fir_sapling", "pine_sapling_small"]
	var placed := 0
	var attempts := 0
	while placed < 36 and attempts < 4000:
		attempts += 1
		var p := _woodland_point(24.0, 64.0)
		var cover := _field.canopy.sample(p.x, p.y)
		if cover < 0.15 or cover > 0.8 or not _allowed(p, 2.2, 0.2):
			continue
		if _trunk_distance(p) < 2.5 or not _spaced("sapling", p, 5.0) or not _camera_clear(p, 1.8):
			continue
		_add(models[placed % 2], p, _rng.randf_range(1.0, 1.7), 0.03, 0.15, false)
		placed += 1


# ------------------------------------------------------------------- rules

func _woodland_point(inner: float, outer: float) -> Vector2:
	var a := _rng.randf() * TAU
	var r := sqrt(lerpf(inner * inner, outer * outer, _rng.randf()))
	return Vector2(cos(a), sin(a)) * r


func _allowed(p: Vector2, trail_clearance: float, water_margin: float) -> bool:
	if p.distance_to(TerrainField.FIRE) < 5.5 or p.distance_to(TerrainField.TENT) < 5.0:
		return false
	if p.distance_to(TerrainField.TABLE) < 2.6 or p.distance_to(TerrainField.WOODPILE) < 2.4:
		return false
	if _field.walking_distance(p) < trail_clearance:
		return false
	if TerrainField.camp_wear(p) > 0.10 and trail_clearance > 1.0:
		return false
	var y := _field.height(p.x, p.y)
	if y < TerrainField.WATER_LEVEL + water_margin:
		return false
	if _field.slope(p.x, p.y) > 0.62:
		return false
	if p.distance_to(TerrainField.DOCK_START) < 4.0:
		return false
	return true


const GRID := 8.0


## Near trunks, planned rocks and camera samples go into 8 m grids once, so the
## thousands of placement attempts only test their neighbourhood.
func _index_obstacles() -> void:
	var radii := {}
	for t in _plan.trees:
		if t.far:
			continue
		if not radii.has(t.kind):
			radii[t.kind] = TreeSpecies.by_kind(t.kind).trunk_radius.y
		_grid_add(_trunk_grid, t.position, [t.position, float(radii[t.kind]) * t.scale])
	for r in _plan.rocks:
		_grid_add(_trunk_grid, r.position, [r.position, r.scale])
	for s in _camera_samples:
		_grid_add(_camera_grid, Vector2(s.x, s.z), s)


static func _grid_add(grid: Dictionary, p: Vector2, value: Variant) -> void:
	var key := Vector2i(floori(p.x / GRID), floori(p.y / GRID))
	if not grid.has(key):
		grid[key] = []
	grid[key].append(value)


## Clearance to the nearest trunk or planned rock within one grid cell (8 m);
## anything farther counts as clear for these small dressings.
func _trunk_distance(p: Vector2) -> float:
	var best := GRID
	var key := Vector2i(floori(p.x / GRID), floori(p.y / GRID))
	for dx in range(-1, 2):
		for dz in range(-1, 2):
			var k := Vector2i(key.x + dx, key.y + dz)
			if not _trunk_grid.has(k):
				continue
			for entry: Array in _trunk_grid[k]:
				best = minf(best, p.distance_to(entry[0]) - float(entry[1]))
	return best


func _spaced(category: String, p: Vector2, spacing: float) -> bool:
	var cell := 4.0
	var key := Vector2i(floori(p.x / cell), floori(p.y / cell))
	var reach := ceili(spacing / cell)
	for dx in range(-reach, reach + 1):
		for dz in range(-reach, reach + 1):
			var k := Vector2i(key.x + dx, key.y + dz)
			if not _occupied.has(k):
				continue
			for entry: Array in _occupied[k]:
				if entry[0] == category and p.distance_to(entry[1]) < spacing:
					return false
	if not _occupied.has(key):
		_occupied[key] = []
	_occupied[key].append([category, p])
	return true


func _camera_clear(p: Vector2, radius: float) -> bool:
	var ground := _field.height(p.x, p.y)
	var key := Vector2i(floori(p.x / GRID), floori(p.y / GRID))
	var reach := ceili(radius / GRID)
	for dx in range(-reach, reach + 1):
		for dz in range(-reach, reach + 1):
			var k := Vector2i(key.x + dx, key.y + dz)
			if not _camera_grid.has(k):
				continue
			for s: Vector3 in _camera_grid[k]:
				if s.y > ground + 3.0:
					continue
				if Vector2(s.x, s.z).distance_to(p) < radius:
					return false
	return true


## Lens positions of every authored camera move: the films, the intro dolly and
## the benchmark path. Large scanned pieces stay out of their way.
static func camera_samples(field: TerrainField, per_shot := 32) -> PackedVector3Array:
	var out := PackedVector3Array()
	for sequence_name in ["showcase", "one_night", "arrival", "pond", "nightfall", "showreel", "afterglow", "storm"]:
		for shot in Cinematic.sequence(sequence_name):
			var path := Spline.new(Cinematic.resolve(field, shot.path, shot.absolute))
			for i in per_shot:
				out.append(Cinematic.camera_position(shot, path, field, shot.duration * float(i) / float(per_shot - 1)))
	var intro := Spline.new(IntroDolly.PATH)
	for i in 80:
		out.append(intro.sample(float(i) / 79.0))
	var bench := Spline.new(CaptureTool.BENCH_PATH)
	for i in 64:
		var p := bench.sample(float(i) / 63.0)
		out.append(Vector3(p.x, field.height(p.x, p.z) + p.y, p.z))
	return out


# ------------------------------------------------------------------- upload

func _add(model: String, p: Vector2, scale: float, sink: float, align: float, collide: bool, yaw := NAN) -> void:
	var list: Array = _variants.get(model, [])
	if list.is_empty():
		return
	var v: ScanVariant = list[_rng.randi() % list.size()]
	var ground := _field.height(p.x, p.y)
	var n := _field.normal(p.x, p.y)
	var up := Vector3.UP.lerp(n, align).normalized()
	var turn := yaw if not is_nan(yaw) else _rng.randf() * TAU
	var basis := Basis(Vector3.UP, turn)
	if up.distance_to(Vector3.UP) > 0.001:
		basis = Basis(Quaternion(Vector3.UP, up)) * basis
	basis = basis.scaled(Vector3.ONE * scale)
	# Scans are authored on their own ground plane; sink them by a fraction of
	# their height so edges bed into the soil instead of hovering.
	var origin := Vector3(p.x, ground - (v.bounds.position.y + sink * v.bounds.size.y) * scale, p.y)
	var xform := Transform3D(basis, origin)
	var key := "%s|%d|%d|%d" % [model, list.find(v), floori(p.x / CELL), floori(p.y / CELL)]
	if not _placements.has(key):
		_placements[key] = {variant = v, transforms = []}
	_placements[key].transforms.append(xform)
	counts[model] = int(counts.get(model, 0)) + 1
	if collide and LIBRARY[model].has("collide"):
		_add_collision(v, xform, LIBRARY[model].collide, LIBRARY[model].kind)


func _add_collision(v: ScanVariant, xform: Transform3D, shape_kind: String, kind: String) -> void:
	var body := StaticBody3D.new()
	body.name = "ScannedCollision"
	body.collision_layer = 1
	body.collision_mask = 0
	body.set_meta("surface", &"wood" if kind == "wood" else &"rock")
	var shape := CollisionShape3D.new()
	var b := v.bounds
	match shape_kind:
		"cylinder":
			var cyl := CylinderShape3D.new()
			cyl.radius = maxf(b.size.x, b.size.z) * 0.42
			cyl.height = b.size.y
			shape.shape = cyl
			shape.position = b.get_center()
		"capsule":
			var cap := CapsuleShape3D.new()
			cap.radius = maxf(b.size.y, b.size.z) * 0.42
			cap.height = maxf(b.size.x, cap.radius * 2.1)
			shape.shape = cap
			shape.position = b.get_center()
			shape.rotation = Vector3(0.0, 0.0, PI * 0.5)
		_:
			var sph := SphereShape3D.new()
			sph.radius = b.size.length() * 0.36
			shape.shape = sph
			shape.position = b.get_center()
	body.transform = xform
	body.add_child(shape)
	add_child(body)


func _upload() -> void:
	for key: String in _placements:
		var group: Dictionary = _placements[key]
		if group.variant == null:
			continue
		var v: ScanVariant = group.variant
		var transforms: Array = group.transforms
		var spec: Dictionary = LIBRARY[v.model]
		var plant: bool = spec.kind == "plant"
		var near := _make_batch(key, v, transforms, spec, true)
		if plant:
			var far := _make_batch(key + "|far", v, transforms, spec, false)
			_ranges.append({node = near, fade = SHADOW_RANGE, begin = 0.0, half = _half(near)})
			_ranges.append({node = far, fade = spec.fade, begin = SHADOW_RANGE, half = _half(far)})
		else:
			_ranges.append({node = near, fade = spec.fade, begin = 0.0, half = _half(near)})


func _make_batch(key: String, v: ScanVariant, transforms: Array, spec: Dictionary, caster: bool) -> MultiMeshInstance3D:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = v.mesh
	mm.instance_count = transforms.size()
	var bounds: AABB = transforms[0] * v.bounds.grow(0.1)
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
		bounds = bounds.merge(transforms[i] * v.bounds.grow(0.1))
	mm.custom_aabb = bounds
	var node := MultiMeshInstance3D.new()
	node.name = "Scanned_" + key.replace("|", "_")
	node.multimesh = mm
	node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON if caster else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	node.gi_mode = GeometryInstance3D.GI_MODE_STATIC if spec.kind != "plant" else GeometryInstance3D.GI_MODE_DISABLED
	# Plants use the cheap grass layer (no mirror pass); solids reflect in the pond.
	node.layers = Pond.GRASS_LAYER if spec.kind == "plant" else 1
	add_child(node)
	batches.append(node)
	return node


static func _half(node: MultiMeshInstance3D) -> float:
	return node.multimesh.custom_aabb.size.length() * 0.5 + 2.0


func _quality(p: QualityPreset) -> void:
	for entry in _ranges:
		var node: MultiMeshInstance3D = entry.node
		var half: float = entry.half
		node.visibility_range_begin = 0.0 if entry.begin <= 0.0 else float(entry.begin) * p.foliage_distance + half - 0.5
		node.visibility_range_end = float(entry.fade) * p.foliage_distance + half
		node.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_DISABLED


func _exit_tree() -> void:
	if Quality.preset_changed.is_connected(_quality):
		Quality.preset_changed.disconnect(_quality)
