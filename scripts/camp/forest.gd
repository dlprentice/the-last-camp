class_name Forest
extends Node3D

## Instantiates the planned trees. Near trees are individual MeshInstance3Ds
## (per-instance LOD, culling and shader parameters, plus trunk collision);
## the distant forest is grouped by variant and spatial cell for culling.

const VARIANTS_PER_SPECIES := 6
const TRUNK_COLLISION_HEIGHT := 5.0
const FAR_CELL_SIZE := 48.0

var field: TerrainField
var plan: ScenePlan
var variants: Dictionary = {}
var bark_materials: Dictionary = {}
var leaf_materials: Dictionary = {}
var far_bark_materials: Dictionary = {}
var far_leaf_materials: Dictionary = {}
var far_leaf_variant_materials: Dictionary = {}
var near_instances: Array[MeshInstance3D] = []
var far_multimeshes: Array[MultiMeshInstance3D] = []
var ridge_leaf_variant_materials: Dictionary = {}
var ridge_multimeshes: Array[MultiMeshInstance3D] = []
var _lod_bark: Dictionary = {}
var _foliage_distance := 1.0
var near_records: Array[Dictionary] = []
var impostor_nodes: Array[Dictionary] = []


func _init(p_field: TerrainField, p_plan: ScenePlan) -> void:
	field = p_field
	plan = p_plan
	name = "Forest"


func build() -> void:
	_build_variants()
	_build_near()
	_build_far()
	Quality.preset_changed.connect(apply_quality)
	apply_quality(Quality.current)


func _build_variants() -> void:
	for kind in TreeSpecies.Kind.values():
		var species: TreeSpecies = TreeSpecies.by_kind(kind)
		var list: Array[TreeGenerator.Result] = []
		for v in VARIANTS_PER_SPECIES:
			var gen := TreeGenerator.new()
			list.append(gen.generate(TreeSpecies.variant(kind, v), 9000 + int(kind) * 100 + v * 17))
		variants[kind] = list
		if not bark_materials.has(species.bark_set):
			bark_materials[species.bark_set] = _make_bark_material(species.bark_set, false)
			far_bark_materials[species.bark_set] = _make_bark_material(species.bark_set, true)
		if species.has_leaves() and not leaf_materials.has(species.leaf_atlas):
			leaf_materials[species.leaf_atlas] = _make_leaf_material(species, false)
			far_leaf_materials[species.leaf_atlas] = _make_leaf_material(species, true)


func _variant_for(entry: ScenePlan.TreeEntry) -> TreeGenerator.Result:
	var list: Array = variants[entry.kind]
	return list[absi(entry.seed_value) % list.size()]


func _tree_transform(entry: ScenePlan.TreeEntry) -> Transform3D:
	var pos := entry.position
	# Keep the authored camp roots fixed; outside the fine inner mesh, roots
	# must follow the rendered triangles rather than the analytic hill height.
	var outer := absf(pos.x) > TerrainBuilder.INNER_UNIFORM_HALF or absf(pos.y) > TerrainBuilder.INNER_UNIFORM_HALF
	var ground := field.surface_height(pos.x, pos.y) if outer else field.height(pos.x, pos.y)
	var y := ground - 0.12 * entry.scale
	var basis := Basis(Vector3.UP, entry.rotation).scaled(Vector3.ONE * entry.scale)
	return Transform3D(basis, Vector3(entry.position.x, y, entry.position.y))


func _build_near() -> void:
	for entry in plan.near_trees():
		var species: TreeSpecies = TreeSpecies.by_kind(entry.kind)
		var result := _variant_for(entry)
		var xform := _tree_transform(entry)
		var tint := _tint_for(entry)

		var bark := MeshInstance3D.new()
		bark.name = "%s_bark" % species.name
		bark.mesh = result.bark
		bark.material_override = bark_materials[species.bark_set]
		bark.transform = xform
		bark.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		bark.gi_mode = GeometryInstance3D.GI_MODE_STATIC
		bark.set_instance_shader_parameter("tree_height", result.height)
		bark.set_instance_shader_parameter("tint", Color(tint.x, tint.y, tint.z))
		add_child(bark)
		near_instances.append(bark)
		var record := {entry = entry, result = result, xform = xform, bark = bark, leaves = null}
		near_records.append(record)

		if result.leaves != null:
			var leaves := MeshInstance3D.new()
			leaves.name = "%s_leaves" % species.name
			leaves.mesh = result.leaves
			leaves.material_override = leaf_materials[species.leaf_atlas]
			leaves.transform = xform
			leaves.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
			leaves.gi_mode = GeometryInstance3D.GI_MODE_STATIC
			leaves.custom_aabb = _leaf_aabb(result)
			leaves.set_instance_shader_parameter("tree_height", result.height)
			leaves.set_instance_shader_parameter("crown",
					Color(result.crown_center.x, result.crown_center.y, result.crown_center.z, result.crown_radius))
			var leaf_tint := species.leaf_tint
			var v := hash_unit(entry.seed_value)
			leaf_tint = leaf_tint.lerp(Color(1.08, 1.0, 0.82), (v - 0.5) * species.leaf_color_variation * 4.0)
			leaves.set_instance_shader_parameter("tint", leaf_tint)
			add_child(leaves)
			near_instances.append(leaves)
			record.leaves = leaves

		_add_trunk_collision(xform, result.trunk_radius * entry.scale, result.height * entry.scale)


func leaf_bounds(result: TreeGenerator.Result) -> AABB:
	return _leaf_aabb(result)


func _leaf_aabb(result: TreeGenerator.Result) -> AABB:
	var r := result.crown_radius * 1.6 + 1.0
	return AABB(Vector3(-r, -0.5, -r), Vector3(r * 2.0, result.height + r, r * 2.0))


func _add_trunk_collision(xform: Transform3D, radius: float, height: float) -> void:
	var body := StaticBody3D.new()
	body.collision_layer = 1
	body.collision_mask = 0
	body.set_meta("surface", &"wood")
	var shape := CollisionShape3D.new()
	var cylinder := CylinderShape3D.new()
	cylinder.radius = maxf(radius * 1.15, 0.12)
	cylinder.height = minf(TRUNK_COLLISION_HEIGHT, height)
	shape.shape = cylinder
	shape.position = Vector3(0.0, cylinder.height * 0.5, 0.0)
	body.add_child(shape)
	body.transform = Transform3D(Basis(), xform.origin)
	add_child(body)


func _build_far() -> void:
	# A world-wide MultiMesh keeps the entire forest alive whenever any part
	# is visible, and its near edge prevents useful mesh LOD. Spatial cells
	# retain the same trees while letting the renderer cull and simplify them.
	var groups: Dictionary = {}
	for entry in plan.far_trees():
		var list: Array = variants[entry.kind]
		var index := absi(entry.seed_value) % list.size()
		var key := "%d_%d_%d_%d" % [int(entry.kind), index,
			floori(entry.position.x / FAR_CELL_SIZE), floori(entry.position.y / FAR_CELL_SIZE)]
		if not groups.has(key):
			groups[key] = {kind = entry.kind, index = index, entries = []}
		groups[key].entries.append(entry)
		var result := _variant_for(entry)
		_add_trunk_collision(_tree_transform(entry), result.trunk_radius * entry.scale, result.height * entry.scale)

	for key in groups:
		var group: Dictionary = groups[key]
		var species: TreeSpecies = TreeSpecies.by_kind(group.kind)
		var result: TreeGenerator.Result = variants[group.kind][group.index]
		var entries: Array = group.entries
		var transforms: Array[Transform3D] = []
		var customs: Array[Color] = []
		for entry in entries:
			transforms.append(_tree_transform(entry))
			customs.append(Color(result.height / 40.0, result.crown_center.y / 40.0,
					result.crown_radius / 20.0, hash_unit(entry.seed_value)))
		_add_far_multimesh(_bark_with_lods(result.bark, "far_%d_%d" % [int(group.kind), group.index]), far_bark_materials[species.bark_set], transforms, customs, true)
		if result.leaves != null:
			# Each batch uses one mesh, so its actual off-centre crown can be
			# shared by that material instead of losing its X/Z to custom data.
			var material_key := "%d_%d" % [int(group.kind), group.index]
			if not far_leaf_variant_materials.has(material_key):
				var variant_mat := far_leaf_materials[species.leaf_atlas].duplicate() as ShaderMaterial
				variant_mat.set_shader_parameter("crown_offset", Vector2(result.crown_center.x, result.crown_center.z))
				far_leaf_variant_materials[material_key] = variant_mat
			var leaf_mat: ShaderMaterial = far_leaf_variant_materials[material_key]
			_add_far_multimesh(result.leaves, leaf_mat, transforms, customs, false)


## The wooded ridges plant the forest's own generated variants out to 655 m:
## the same bark and leaf meshes, batched per 160 m cell, with bark mesh LODs
## and a leaf-card thinning that only starts once cards are below a pixel.
func add_ridge_group(kind: TreeSpecies.Kind, result: TreeGenerator.Result, material_key: String,
		transforms: Array[Transform3D]) -> void:
	var species: TreeSpecies = TreeSpecies.by_kind(kind)
	var customs: Array[Color] = []
	for i in transforms.size():
		customs.append(Color(result.height / 40.0, result.crown_center.y / 40.0,
				result.crown_radius / 20.0, hash_unit(i * 7919 + material_key.hash() % 1000 + int(kind) * 17)))
	_add_far_multimesh(_bark_with_lods(result.bark, "ridge_" + material_key), far_bark_materials[species.bark_set], transforms, customs, true, true)
	if result.leaves != null:
		if not ridge_leaf_variant_materials.has(material_key):
			var variant_mat := far_leaf_materials[species.leaf_atlas].duplicate() as ShaderMaterial
			variant_mat.set_shader_parameter("crown_offset", Vector2(result.crown_center.x, result.crown_center.z))
			variant_mat.set_shader_parameter("lod_start", RIDGE_LEAF_LOD[0])
			variant_mat.set_shader_parameter("lod_end", RIDGE_LEAF_LOD[1])
			variant_mat.set_shader_parameter("lod_keep", RIDGE_LEAF_LOD[2])
			variant_mat.set_shader_parameter("lod_grow", RIDGE_LEAF_LOD[3])
			ridge_leaf_variant_materials[material_key] = variant_mat
		_add_far_multimesh(result.leaves, ridge_leaf_variant_materials[material_key], transforms, customs, false, true)


## Distant bark keeps its silhouette through generated mesh LODs (the
## renderer picks a level by screen size); the tubes are two thirds of a
## tree's triangles and never need them at three hundred metres.
func _bark_with_lods(source: ArrayMesh, key: String) -> ArrayMesh:
	if _lod_bark.has(key):
		return _lod_bark[key]
	var importer := ImporterMesh.new()
	for surface in source.get_surface_count():
		importer.add_surface(source.surface_get_primitive_type(surface), source.surface_get_arrays(surface),
				[], {}, null, "", source.surface_get_format(surface))
	importer.generate_lods(25.0, 60.0, [])
	var mesh := importer.get_mesh()
	if mesh == null or mesh.get_surface_count() != source.get_surface_count():
		mesh = source
	_lod_bark[key] = mesh
	return mesh


func _add_far_multimesh(mesh: ArrayMesh, material: Material, transforms: Array[Transform3D],
		customs: Array[Color], is_bark: bool, ridge := false) -> void:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.mesh = mesh
	mm.instance_count = transforms.size()
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
		mm.set_instance_custom_data(i, customs[i])
	# GPU sway and the retained LOD sprays extend beyond the rest mesh.
	if not is_bark:
		var bounds := transforms[0] * mesh.get_aabb().grow(1.5)
		for i in range(1, transforms.size()):
			bounds = bounds.merge(transforms[i] * mesh.get_aabb().grow(1.5))
		mm.custom_aabb = bounds
	var mmi := MultiMeshInstance3D.new()
	mmi.name = "%s_%s" % ["Ridge" if ridge else "FarTrees", "bark" if is_bark else "leaves"]
	mmi.multimesh = mm
	mmi.material_override = material
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	# Static for the ridges too: the outer SDFGI cascades reach the nearer
	# band, and occluding the sky between crowns is what keeps a canopy dark.
	mmi.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	add_child(mmi)
	if ridge:
		ridge_multimeshes.append(mmi)
	else:
		far_multimeshes.append(mmi)


# ---------------------------------------------------------------- materials

func _make_bark_material(bark_set: String, far: bool) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/bark.gdshader")
	Camp.bind_texture(mat, "albedo_tex", "res://textures/%s_albedo.png" % bark_set)
	Camp.bind_texture(mat, "normal_tex", "res://textures/%s_normal.png" % bark_set)
	Camp.bind_texture(mat, "orm_tex", "res://textures/%s_orm.png" % bark_set)
	Camp.bind_texture(mat, "noise_tex", "res://textures/noise_rgba.png")
	mat.set_shader_parameter("use_instance_custom", far)
	mat.set_shader_parameter("moss_amount", 0.5 if bark_set == "bark_oak" else 0.25)
	return mat


func _make_leaf_material(species: TreeSpecies, far: bool) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/foliage.gdshader")
	Camp.bind_texture(mat, "albedo_tex", "res://textures/%s.png" % species.leaf_atlas)
	Camp.bind_texture(mat, "normal_trans_tex", "res://textures/%s_nt.png" % species.leaf_atlas)
	mat.set_shader_parameter("use_instance_custom", far)
	var conifer := species.kind == TreeSpecies.Kind.SPRUCE or species.kind == TreeSpecies.Kind.PINE
	mat.set_shader_parameter("translucency", 0.24 if conifer else 0.36)
	# Rounder crown shading: a stronger share of the crown-centred normal
	# reads the canopy as a lit volume at mid distance instead of flat sprays.
	mat.set_shader_parameter("spherical_normal_mix", 0.62 if conifer else 0.66)
	mat.set_shader_parameter("flutter", 0.06 if conifer else 0.14)
	mat.set_shader_parameter("roughness", 0.62 if conifer else 0.5)
	return mat


func _tint_for(entry: ScenePlan.TreeEntry) -> Vector3:
	var v := hash_unit(entry.seed_value + 7)
	return Vector3(0.92, 0.92, 0.92).lerp(Vector3(1.06, 1.02, 0.98), v)


static func hash_unit(value: int) -> float:
	var h := absi(value * 2654435761) % 100000
	return float(h) / 100000.0


## Leaf-card LOD: [start, end, share collapsed at full LOD, survivor growth].
## Overlapping alpha-tested cards are the largest cost in the frame (see the
## profiler's overdraw view), but collapsing cards while growing the survivors
## measured neutral, so these stay close to the original look; the far forest
## thins slightly more because its cards only ever fill silhouettes.
const NEAR_LEAF_LOD := [40.0, 115.0, 0.52, 0.16]
const FAR_LEAF_LOD := [40.0, 110.0, 0.62, 0.30]
## The ridge variants are already thinned at build time; the shader only
## collapses a little more far out and barely grows the survivors, or the
## crowns turn into bright shells of oversized cards.
const RIDGE_LEAF_LOD := [180.0, 420.0, 0.30, 0.20]


## Beyond the switch distance each hero tree hands over to a baked billboard
## and a sun-facing shadow card; within it the hero meshes render as before.
func attach_impostors(impostors: TreeImpostors) -> void:
	if impostors.baked.is_empty():
		return
	for record in near_records:
		var entry: ScenePlan.TreeEntry = record.entry
		var list: Array = variants[entry.kind]
		var index := absi(entry.seed_value) % list.size()
		var b: TreeImpostors.Baked = impostors.baked.get(TreeImpostors.key_for(entry.kind, index))
		if b == null:
			continue
		var xform: Transform3D = record.xform
		var placement := Transform3D(Basis.IDENTITY.scaled(Vector3.ONE * entry.scale), xform.origin)
		var card := MeshInstance3D.new()
		card.name = "%s_impostor" % TreeSpecies.by_kind(entry.kind).name
		card.mesh = b.quad
		card.material_override = b.material
		card.transform = placement
		card.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		card.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
		card.set_instance_shader_parameter("yaw", entry.rotation)
		add_child(card)
		var shadow := MeshInstance3D.new()
		shadow.name = "%s_impostor_shadow" % TreeSpecies.by_kind(entry.kind).name
		shadow.mesh = b.quad
		shadow.material_override = b.shadow_material
		shadow.transform = placement
		shadow.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_SHADOWS_ONLY
		shadow.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
		shadow.set_instance_shader_parameter("yaw", entry.rotation)
		add_child(shadow)
		impostor_nodes.append({bark = record.bark, leaves = record.leaves, card = card, shadow = shadow})
	_apply_switch_distance()


var _switch_override := -1.0


## Profiler hook: force the hero/impostor switch distance (negative restores the preset's).
func set_switch_distance(distance: float) -> void:
	_switch_override = distance
	_apply_switch_distance()


func _apply_switch_distance() -> void:
	var d := TreeImpostors.SWITCH_DISTANCE * _foliage_distance if _switch_override < 0.0 else _switch_override
	for n in impostor_nodes:
		for hero in [n.bark, n.leaves]:
			if hero == null:
				continue
			var mi: MeshInstance3D = hero
			mi.visibility_range_end = d
			mi.visibility_range_end_margin = TreeImpostors.SWITCH_MARGIN
			mi.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF
		var card: MeshInstance3D = n.card
		card.visibility_range_begin = d
		card.visibility_range_begin_margin = TreeImpostors.SWITCH_MARGIN
		card.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF
		var shadow: MeshInstance3D = n.shadow
		shadow.visibility_range_begin = d
		shadow.visibility_range_begin_margin = 0.0


func apply_quality(p: QualityPreset) -> void:
	_foliage_distance = p.foliage_distance
	set_leaf_lod(NEAR_LEAF_LOD, FAR_LEAF_LOD, p.foliage_distance)
	if not impostor_nodes.is_empty():
		_apply_switch_distance()


func set_leaf_lod(near_lod: Array, far_lod: Array, distance_scale: float) -> void:
	for key in leaf_materials:
		var mat: ShaderMaterial = leaf_materials[key]
		mat.set_shader_parameter("lod_start", near_lod[0] * distance_scale)
		mat.set_shader_parameter("lod_end", near_lod[1] * distance_scale)
		mat.set_shader_parameter("lod_keep", near_lod[2])
		mat.set_shader_parameter("lod_grow", near_lod[3])
	for mat_dict in [far_leaf_materials, far_leaf_variant_materials]:
		for key in mat_dict:
			var mat: ShaderMaterial = mat_dict[key]
			mat.set_shader_parameter("lod_start", far_lod[0] * distance_scale)
			mat.set_shader_parameter("lod_end", far_lod[1] * distance_scale)
			mat.set_shader_parameter("lod_keep", far_lod[2])
			mat.set_shader_parameter("lod_grow", far_lod[3])
