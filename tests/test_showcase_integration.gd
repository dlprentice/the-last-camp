extends TestCase

func test_new_plant_forms_are_deterministic_finite_and_distinct() -> void:
	var sizes: Array[int] = []
	for form in 4:
		var first := HabitatDiversity.plant_mesh(form, 61000 + form)
		var again := HabitatDiversity.plant_mesh(form, 61000 + form)
		var vertices: PackedVector3Array = first.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
		var normals: PackedVector3Array = first.surface_get_arrays(0)[Mesh.ARRAY_NORMAL]
		assert_eq(vertices, again.surface_get_arrays(0)[Mesh.ARRAY_VERTEX], "plant meshes reproduce exactly")
		assert_gt(vertices.size(), 100, "form has authored leaf/stem geometry")
		for i in vertices.size():
			assert_true(vertices[i].is_finite() and normals[i].is_finite(), "plant geometry has finite positions/normals")
			assert_gt(normals[i].length_squared(), 0.3, "plant surface has a usable normal")
		if not sizes.has(vertices.size()):
			sizes.append(vertices.size())
	assert_eq(sizes.size(), 4, "four different topologies, not four tints of a grass card")

func test_habitat_plan_is_repeatable_and_navigation_stays_clear() -> void:
	var field := TerrainFieldEnhanced.new()
	var scene := ScenePlan.new(field)
	scene.build()
	var first := HabitatDiversity.plan(field, 6000)
	var second := HabitatDiversity.plan(field, 6000)
	assert_eq(first, second, "seeded habitat layout is deterministic")
	var counts := [0, 0, 0, 0]
	for key: Vector4i in first:
		for item: Dictionary in first[key]:
			var frame: Transform3D = item.frame
			var p := Vector2(frame.origin.x, frame.origin.z)
			var width := frame.basis.x.length()
			assert_true(HabitatDiversity.placement_allowed(field, p, 0.48 * width), "the whole small-plant footprint clears travelled routes")
			assert_eq(floori(p.x / HabitatDiversity.CELL_SIZE), key.x, "spatial batch X matches root")
			assert_eq(floori(p.y / HabitatDiversity.CELL_SIZE), key.y, "spatial batch Z matches root")
			counts[key.z] += 1
	for count in counts:
		assert_gt(count, 5, "each habitat form is actually represented")

func test_root_profiles_taper_and_meet_the_actual_surface() -> void:
	var field := TerrainFieldEnhanced.new()
	var curve := ForestFloorDressing.root_curve(field, Vector2(42, -28), Vector2(0.6, 0.8), 1.6, 0.19, 0.13)
	assert_eq(curve.points.size(), 11, "curved root sample count")
	for i in curve.points.size():
		var point: Vector3 = curve.points[i]
		assert_true(point.is_finite(), "finite root point")
		assert_lt(point.y, field.surface_height(point.x, point.z), "root centre is embedded, not hovering")
		if i > 0:
			assert_lt(curve.radii[i], curve.radii[i - 1], "root radius continuously tapers")
	assert_lt(curve.radii[-1], 0.004, "tip disappears into earth")
	assert_eq(PropMaterials.bark().get_shader_parameter("wind_response"), 0.0, "deadwood/roots do not sway like upright trees")

func test_fractured_timber_keeps_bark_and_end_grain_surfaces() -> void:
	var mesh := ForestFloorDressing.fractured_log(0.14, 9801)
	assert_eq(mesh.get_surface_count(), 2, "bark and exposed wood keep separate material slots")
	for surface in mesh.get_surface_count():
		for point: Vector3 in mesh.surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]:
			assert_true(point.is_finite(), "fractured timber has finite geometry")
		assert_gt(mesh.surface_get_array_index_len(surface), 20, "surface is renderable")

func test_photo_movement_and_focus_have_consistent_direction_and_timing() -> void:
	assert_eq(PhotoRig.move_direction(Basis.IDENTITY, Vector3(0, 0, -1)), Vector3.FORWARD, "W moves along Godot -Z")
	assert_eq(PhotoRig.move_direction(Basis.IDENTITY, Vector3(0, 0, 1)), Vector3.BACK, "S moves backward")
	assert_near(PhotoRig.move_direction(Basis.IDENTITY, Vector3(1, 1, -1)).length(), 1.0, 1e-6, "diagonal flight has no speed bonus")
	for fps in [24, 60, 144]:
		var distance := 2.0
		for frame in fps:
			distance = PhotoRig.focus_step(distance, 12.0, 1.0 / fps)
		assert_near(distance, 12.0 - 10.0 * exp(-5.0), 0.00001, "focus pull is frame-rate independent")
	assert_eq(PhotoRig.focus_step(2, 12, 0), 2.0, "paused focus does not advance")

func test_shelter_audio_uses_the_visible_roof_volume() -> void:
	assert_true(CampWeather.tent_shelter(Vector3(0, 0.7, 0)), "listener under roof is sheltered")
	assert_false(CampWeather.tent_shelter(Vector3(0, 1.8, 0)), "above ridge is exposed")
	assert_false(CampWeather.tent_shelter(Vector3(1.3, 0.4, 0)), "beside tent is exposed")
	assert_false(CampWeather.tent_shelter(Vector3(0, 0.5, 1.7)), "beyond tent end is exposed")

func test_canopy_shedding_is_a_post_rain_effect() -> void:
	assert_near(CanopyDrips.post_rain_strength(1, 0), 1, 1e-6, "wet canopy drips after rain")
	assert_near(CanopyDrips.post_rain_strength(1, 1), 0, 1e-6, "full rain does not double with after-rain drops")
	assert_near(CanopyDrips.post_rain_strength(0, 0), 0, 1e-6, "dry canopy stays quiet")

func test_wildlife_habitats_are_distributed_but_remain_real_scale() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var life := Wildlife.new(TerrainFieldEnhanced.new())
	tree.root.add_child(life)
	life.set_process(false)
	life._build_small_life()
	assert_eq(life._butterflies.size(), 12, "six butterfly habitat pairs")
	assert_eq(life._dragonflies.size(), 12, "six pond-edge dragonfly pairs")
	assert_gt(life.fish_routes.size(), 11, "multiple schools have safe sampled routes")
	assert_gt(life._butterfly_habitats[0].distance_to(life._butterfly_habitats[-1]), 15, "habitats are not all in the same tiny patch")
	life._life_clock = 7.0
	life._update_small_life(1, 1, 0.4)
	for bird in life._birds:
		assert_near(bird.scale.distance_to(Vector3.ONE), 0, 0.00001, "autonomous birds no longer grow/shrink on arrival")
	life.free()

func test_showcase_camera_path_clears_enhanced_scene() -> void:
	var field := TerrainFieldEnhanced.new()
	var plan := ScenePlan.new(field)
	plan.build()
	var tree := Engine.get_main_loop() as SceneTree
	var dock := Dock.new(field)
	tree.root.add_child(dock)
	dock.build()
	var piles: Array[Vector3] = []
	for top in dock._piles:
		piles.append(dock.to_global(top))
	var hits := Cinematic.obstructions(field, plan, "showcase", piles, dock.canoe.position, dock.canoe.rotation.y)
	assert_eq(hits.size(), 0, "showcase camera obstruction: %s" % ", ".join(hits.slice(0, 5)))
	# The dock parents its canoe beside itself rather than under the jetty.
	dock.canoe.free()
	dock.free()
