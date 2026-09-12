class_name AssetReview
extends Node3D

## Visual QA of the actual generated meshes and materials. Four studio angles
## reveal construction; two contextual angles reveal scale and placement.
## Output is deliberately local and reviewable, never part of the game HUD.
var camera: Camera3D
var studio: Node3D
var sources: Node3D
var studio_environment: Environment
var entries: Array[Dictionary] = []
var output: String
var _metadata: Array[Dictionary] = []
const VIEWS := [[35.0, 18.0], [125.0, 24.0], [215.0, 20.0], [305.0, 62.0]]


func run(directory: String) -> void:
	output = ProjectSettings.globalize_path(directory)
	DirAccess.make_dir_recursive_absolute(output)
	Game.hud_visible = false
	Game.mode = Game.Mode.PHOTO
	camera = Camera3D.new()
	camera.near = 0.015
	camera.far = 1800
	camera.fov = 44
	camera.cull_mask = 0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)
	add_child(camera)
	camera.make_current()
	_build_studio()
	_inventory()
	await get_tree().create_timer(1.6).timeout
	var filter := Game.arg_value("assets", "").split(",", false)
	print("ASSET_REVIEW_START entries=%d" % entries.size())
	for entry in entries:
		if not filter.is_empty() and not filter.has(entry.name):
			continue
		await _review(entry)
	var file := FileAccess.open(output.path_join("inventory.json"), FileAccess.WRITE)
	file.store_string(JSON.stringify(_metadata, "  "))
	file.close()
	print("ASSET_REVIEW_DONE assets=%d output=%s" % [_metadata.size(), output])
	get_tree().quit()


func _build_studio() -> void:
	sources = Node3D.new()
	sources.name = "ReviewSources"
	sources.visible = false
	add_child(sources)
	studio = Node3D.new()
	studio.name = "ReviewStudio"
	add_child(studio)
	studio_environment = Environment.new()
	studio_environment.background_mode = Environment.BG_COLOR
	studio_environment.background_color = Color(0.15, 0.17, 0.18)
	studio_environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	studio_environment.ambient_light_color = Color(0.86, 0.9, 1.0)
	studio_environment.ambient_light_energy = 0.65
	studio_environment.tonemap_mode = Environment.TONE_MAPPER_AGX
	studio_environment.ssao_enabled = true
	studio_environment.ssao_radius = 0.25
	studio_environment.ssao_intensity = 1.0
	var key := DirectionalLight3D.new()
	key.light_energy = 2.5
	key.rotation_degrees = Vector3(-38, -35, 0)
	key.shadow_enabled = true
	key.directional_shadow_max_distance = 160
	studio.add_child(key)
	var fill := DirectionalLight3D.new()
	fill.light_color = Color(0.72, 0.82, 1.0)
	fill.light_energy = 0.6
	fill.rotation_degrees = Vector3(-22, 135, 0)
	studio.add_child(fill)
	var plane := MeshInstance3D.new()
	var mesh := PlaneMesh.new()
	mesh.size = Vector2(400, 400)
	plane.mesh = mesh
	plane.position.y = -0.015
	plane.material_override = FieldKit.solid(Color(0.29, 0.31, 0.32), 0.88)
	studio.add_child(plane)


func _add(name_value: String, source: Node3D) -> void:
	if source == null:
		push_error("Missing review model: " + name_value)
		return
	var local := _bounds(source, source.transform.affine_inverse())
	var placement: Transform3D = source.get_parent().global_transform if source is SoftBody3D else source.global_transform
	entries.append({name = name_value, source = source, local_bounds = local,
		context_bounds = placement * local})


func _inventory() -> void:
	var camp: Camp = Game.camp
	# Passing birds scale away between flights. Inspect a full-size snapshot,
	# rather than framing the almost invisible idle pose.
	var bird := _copy_visual(camp.wildlife.get_node("PondBird0"))
	bird.scale = Vector3.ONE
	sources.add_child(bird)
	_add("pond_bird", bird)
	if camp.wildlife.has_node("PondFish0"):
		_add("pond_fish", camp.wildlife.get_node("PondFish0"))
	# Every generated tree variant, located at one of its real planted instances.
	for kind in TreeSpecies.Kind.values():
		var species := TreeSpecies.by_kind(kind)
		for variant in Forest.VARIANTS_PER_SPECIES:
			var result: TreeGenerator.Result = camp.forest.variants[kind][variant]
			var root := Node3D.new()
			sources.add_child(root)
			for t in camp.plan.trees:
				if t.kind == kind and absi(t.seed_value) % Forest.VARIANTS_PER_SPECIES == variant:
					root.transform = camp.forest._tree_transform(t)
					break
			var bark := FieldKit.add(root, result.bark, camp.forest.bark_materials[species.bark_set])
			bark.set_instance_shader_parameter("tree_height", result.height)
			if result.leaves != null:
				var leaf := FieldKit.add(root, result.leaves, camp.forest.leaf_materials[species.leaf_atlas])
				leaf.set_instance_shader_parameter("tree_height", result.height)
				leaf.set_instance_shader_parameter("crown", Color(result.crown_center.x, result.crown_center.y, result.crown_center.z, result.crown_radius))
				leaf.set_instance_shader_parameter("tint", species.leaf_tint)
			_add("tree_%s_%d" % [species.name, variant + 1], root)
	# Actual vegetation meshes, sampled from their planted MultiMeshes.
	for name_value in ["Ferns", "Shrubs", "Meadow_0", "Meadow_1", "Meadow_2", "Meadow_3", "MeadowSeedHeads", "Reeds"]:
		_single(name_value.to_lower(), _understory_batch(camp.understory, name_value), Vector2(6, 8))
	for species in 4:
		for variant in range(1, 3):
			var node_name := "Meadow_%d_variant_%d" % [species, variant]
			_single(node_name.to_lower(), camp.understory.get_node(node_name), Vector2(6, 8))
	var grass_patch: MultiMeshInstance3D = camp.understory.get_node("Grass_0_8")
	_single("grass_clump", grass_patch, Vector2(6, 8))
	# A local patch exposes spacing and repeated silhouettes as well as blades.
	var patch := _copy_visual(grass_patch)
	sources.add_child(patch)
	_add("grass_patch", patch)
	for name_value in ["LilyPads", "WaterLilies", "LilyStems", "WaterwornStones"]:
		_single(name_value.to_lower(), camp.shore_life.get_node(name_value), Vector2(-28, 15))
	var reviewed_rocks := {}
	for child in camp.rocks.get_children():
		if child is MeshInstance3D:
			var variant := camp.rocks.variants.find(child.mesh)
			if not reviewed_rocks.has(variant):
				_add("boulder_%d" % (variant + 1), child)
				reviewed_rocks[variant] = true
	# The wooded ridges plant the forest's own variants, reviewed above.
	var site := camp.campsite
	_add("tent", site.tent)
	_add("backpack", site.tent.get_node("WaxedCanvasPack"))
	_add("groundsheet", site.tent.get_node("GroundSheet"))
	_add("bedroll", site.tent.get_node("Bedroll"))
	_add("hanging_lantern", site.tent.lantern)
	_add("post_lantern", site.lanterns[2])
	_add("dock_lantern", site.dock.lantern)
	_add("cooking_corner", site.kitchen)
	_add("tablecloth", site.kitchen.cloth)
	for name_value in ["CoffeePot", "EnamelMug", "EnamelMug2", "FieldJournal", "SlattedSupplyCrate"]:
		_add(name_value.to_snake_case(), site.kitchen.get_node(name_value))
	_add("blanket", site.kitchen.get_node("SlattedSupplyCrate/RolledWoolBlanket"))
	for i in 3:
		_add("bench_%d" % (i + 1), site.get_node("SplitLogBench_%d" % i))
	var pile := site.get_node("Woodpile")
	_add("woodpile", pile)
	_add("chopping_block", pile.get_node("ChoppingBlock"))
	_add("axe", pile.get_node("Axe"))
	_add("firepit", site.firepit)
	_add("hanging_pot", site.firepit.get_node("Pot"))
	_add("tripod", site.firepit.get_node("Tripod"))
	_add("coals", site.firepit.get_node("Coals"))
	_add("dock", site.dock)
	_add("canoe", site.dock.canoe)
	_add("deck", site.dock.get_node("Deck"))
	_add("mooring", site.dock.get_node("Mooring"))
	_add("skipping_stones", site.dock.get_node("SkippingStones"))


func _single(name_value: String, source: MultiMeshInstance3D, near: Vector2) -> void:
	var mm := source.multimesh
	if mm.instance_count == 0:
		push_error("Empty vegetation stand: " + name_value)
		return
	var best := INF
	var selected := 0
	for i in mm.instance_count:
		var at := mm.get_instance_transform(i).origin
		var distance := Vector2(at.x, at.z).distance_squared_to(near)
		if distance < best:
			best = distance
			selected = i
	var one := MultiMesh.new()
	one.transform_format = MultiMesh.TRANSFORM_3D
	one.mesh = mm.mesh
	one.use_colors = mm.use_colors
	one.use_custom_data = mm.use_custom_data
	one.instance_count = 1
	one.set_instance_transform(0, Transform3D.IDENTITY)
	if mm.use_colors:
		one.set_instance_color(0, mm.get_instance_color(selected))
	if mm.use_custom_data:
		one.set_instance_custom_data(0, mm.get_instance_custom_data(selected))
	var root := MultiMeshInstance3D.new()
	root.multimesh = one
	root.material_override = source.material_override
	sources.add_child(root)
	root.global_transform = source.global_transform * mm.get_instance_transform(selected)
	_add(name_value, root)


func _copy_visual(source: Node3D) -> Node3D:
	var root: Node3D
	if source is SoftBody3D:
		var mi := MeshInstance3D.new()
		mi.mesh = _soft_mesh(source)
		mi.material_override = source.material_override
		root = mi
	elif source is MeshInstance3D:
		var mi := MeshInstance3D.new()
		mi.mesh = source.mesh
		mi.material_override = source.material_override
		for i in source.get_surface_override_material_count():
			mi.set_surface_override_material(i, source.get_surface_override_material(i))
		if source.material_override is ShaderMaterial:
			var code: String = source.material_override.shader.code
			for parameter in ["tree_height", "crown", "tint"]:
				if code.contains("instance uniform") and source.get_instance_shader_parameter(parameter) != null:
					mi.set_instance_shader_parameter(parameter, source.get_instance_shader_parameter(parameter))
		root = mi
	elif source is MultiMeshInstance3D:
		var mi := MultiMeshInstance3D.new()
		mi.multimesh = source.multimesh
		mi.material_override = source.material_override
		root = mi
	elif source is Label3D:
		root = source.duplicate()
	else:
		root = Node3D.new()
	root.transform = source.transform
	for child in source.get_children():
		if child is Node3D and not child is Light3D and not child is GPUParticles3D and not child is CollisionShape3D:
			root.add_child(_copy_visual(child))
	return root


## SoftBody3D becomes top-level and exposes simulated points in world space.
## Snapshot those points back into the owning prop's frame for studio review.
func _soft_mesh(source: SoftBody3D) -> ArrayMesh:
	var arrays := source.mesh.surface_get_arrays(0)
	var mb := MeshBuilder.new()
	var owner_inverse: Transform3D = source.get_parent().global_transform.affine_inverse()
	mb.vertices.resize(arrays[Mesh.ARRAY_VERTEX].size())
	for i in mb.vertices.size():
		mb.vertices[i] = owner_inverse * source.get_point_transform(i)
	mb.uvs = arrays[Mesh.ARRAY_TEX_UV]
	mb.normals = arrays[Mesh.ARRAY_NORMAL]
	mb.colors = arrays[Mesh.ARRAY_COLOR]
	mb.indices = arrays[Mesh.ARRAY_INDEX]
	mb.recompute_normals()
	mb.recompute_tangents()
	return mb.commit()


func _bounds(root: Node3D, parent_transform := Transform3D.IDENTITY) -> AABB:
	var transform_here := parent_transform * root.transform
	var bounds := AABB()
	if root is SoftBody3D and root.mesh != null:
		bounds = transform_here * _soft_mesh(root).get_aabb()
	elif root is MeshInstance3D and root.mesh != null:
		bounds = transform_here * root.mesh.get_aabb()
	elif root is MultiMeshInstance3D and root.multimesh != null:
		bounds = transform_here * root.multimesh.get_aabb()
	for child in root.get_children():
		if child is Node3D:
			var child_bounds := _bounds(child, transform_here)
			if child_bounds.size.length_squared() > 0:
				bounds = child_bounds if bounds.size.length_squared() == 0 else bounds.merge(child_bounds)
	return bounds


func _frame(bounds: AABB, azimuth: float, elevation: float, contextual: bool) -> void:
	var centre := bounds.get_center()
	var size := maxf(bounds.size.x, maxf(bounds.size.y, bounds.size.z))
	var distance := maxf(size * 1.85, 1.4 if contextual else 0.25)
	var az := deg_to_rad(azimuth)
	var el := deg_to_rad(elevation)
	var at := centre + Vector3(cos(az) * cos(el), sin(el), sin(az) * cos(el)) * distance
	if contextual:
		at.y = maxf(at.y, Game.camp.field.height(at.x, at.z) + 0.4)
	camera.global_position = at
	camera.look_at(centre, Vector3.UP)


func _review(entry: Dictionary) -> void:
	var paths: Array[String] = []
	var source: Node3D = entry.source
	var isolated := _copy_visual(source)
	isolated.transform = Transform3D(Basis.from_scale(source.global_transform.basis.get_scale()), Vector3.ZERO)
	studio.add_child(isolated)
	var bounds := _bounds(isolated)
	isolated.position = -Vector3(bounds.get_center().x, bounds.position.y, bounds.get_center().z)
	bounds = _bounds(isolated)
	Game.camp.visible = false
	# Native soft bodies are top-level, so their visibility does not inherit
	# the hidden camp. Exclude the live body while its snapshot is on stage.
	var live_cloth: SoftBody3D = Game.camp.campsite.kitchen.cloth
	var cloth_layers := live_cloth.layers
	live_cloth.layers = 0
	Game.camp.process_mode = Node.PROCESS_MODE_DISABLED
	Game.world.visible = false
	Game.world.set_process(false)
	Game.world.post.set_enabled(false)
	studio.visible = true
	camera.environment = studio_environment
	RenderingServer.global_shader_parameter_set("wind_strength", 0.0)
	# Grass in the studio must not dissolve or flatten around the old player.
	RenderingServer.global_shader_parameter_set("player_position", Vector3(9000, 9000, 9000))
	for i in VIEWS.size():
		_frame(bounds, VIEWS[i][0], VIEWS[i][1], false)
		paths.append(await _capture(entry.name, "isolated_%d" % (i + 1)))
	isolated.free()
	Game.camp.visible = true
	live_cloth.layers = cloth_layers
	Game.camp.process_mode = Node.PROCESS_MODE_INHERIT
	Game.world.visible = true
	Game.world.set_process(true)
	Game.world.post.set_enabled(true)
	Game.world.hour = 19.05
	studio.visible = false
	camera.environment = null
	for i in 2:
		_frame(entry.context_bounds, 60.0 + float(i) * 155.0, 22.0, true)
		if entry.name in ["backpack", "groundsheet", "bedroll", "hanging_lantern"]:
			# Look through the doorway; exterior orbit cameras see the canvas.
			var eye := Vector3(0.12, 0.78, -2.15) if i == 0 else Vector3(-0.24, 0.68, -1.28)
			camera.global_position = Game.camp.campsite.tent.to_global(eye)
			camera.look_at(entry.context_bounds.get_center(), Vector3.UP)
		paths.append(await _capture(entry.name, "placed_%d" % (i + 1)))
	_metadata.append({name = entry.name, source = str(source.get_path()), images = paths})
	print("ASSET_REVIEW %s views=%d" % [entry.name, paths.size()])


func _capture(asset: String, view_name: String) -> String:
	var elapsed := 0.0
	var frames := 0
	while elapsed < 0.65 or frames < 12:
		await get_tree().process_frame
		elapsed += get_process_delta_time()
		frames += 1
	await RenderingServer.frame_post_draw
	var path := output.path_join(asset + "__" + view_name + ".png")
	var error := get_viewport().get_texture().get_image().save_png(path)
	if error != OK:
		push_error("Could not save asset review: " + path)
		get_tree().quit(1)
	return path


## Vegetation batches are split per cell (for example `Ferns_3_-2`); review the
## first cell when no batch carries the plain name.
static func _understory_batch(understory: Node, name_value: String) -> MultiMeshInstance3D:
	var direct := understory.get_node_or_null(name_value)
	if direct != null:
		return direct
	for child in understory.get_children():
		if child is MultiMeshInstance3D and child.name.begins_with(name_value + "_"):
			return child
	push_error("No understory batch named %s" % name_value)
	return null
