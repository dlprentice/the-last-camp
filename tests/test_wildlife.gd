extends TestCase


func test_bird_perch_uses_the_real_post_cap() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var holder := Node3D.new()
	tree.root.add_child(holder)
	var field := TerrainField.new()
	var dock := Dock.new(field)
	holder.add_child(dock)
	dock.build()
	var wildlife := Wildlife.new(field)
	holder.add_child(wildlife)
	wildlife._prepare_bird_perch(dock)
	assert_true(wildlife._perch_ready, "exposed post provides a usable real perch")
	var foot := dock.to_local(wildlife.bird_perch_position())
	var mesh: TriangleMesh = dock.get_node("Piles").mesh.generate_triangle_mesh()
	var hit := mesh.intersect_ray(foot + Vector3.UP * 0.1, Vector3.DOWN)
	assert_false(hit.is_empty(), "bird's feet have an actual timber face below")
	if not hit.is_empty():
		assert_near(foot.y, hit.position.y, 0.00005, "foot anchor touches rendered cap")
	assert_lt(Vector2(foot.x - dock._piles[4].x, foot.z - dock._piles[4].z).length(), 0.002, "perch stays on the cap centre")
	holder.free()


func test_bird_cue_is_shot_timed_quiet_elsewhere_and_rain_gated() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var wildlife := Wildlife.new(TerrainField.new())
	tree.root.add_child(wildlife)
	wildlife.set_process(false)
	wildlife._build_small_life()
	wildlife._perch_ready = true
	wildlife._bird_perch = Vector3(-19.7, 0.2, 6.6)
	wildlife.set_film_cue(Wildlife.BIRD_CUE, 2.0)
	wildlife._update_small_life(1.0, 1.0, 0.4)
	assert_true(wildlife._birds[0].visible, "first bird can be observed on the post")
	assert_true(wildlife._birds[0].get_node("Perched").visible, "resting bird uses folded wings and feet")
	assert_false(wildlife._birds[1].visible, "companion does not start early")
	wildlife.set_film_cue(Wildlife.BIRD_CUE, 9.0)
	wildlife._update_small_life(1.0, 1.0, 0.4)
	var before := wildlife._birds[0].transform
	wildlife._life_clock = 731.0
	wildlife._bird_wind_phase = 99.0
	wildlife._update_small_life(1.0, 1.0, 0.4)
	assert_eq(wildlife._birds[0].transform, before, "loading/autonomous clocks do not move the film bird")
	assert_true(wildlife._birds[1].visible, "companion appears during its authored pass")
	assert_lt(wildlife._birds[0].scale.distance_to(Vector3.ONE), 0.00001, "film does not enlarge the bird")
	wildlife.set_film_cue(&"", 9.0)
	wildlife._update_small_life(1.0, 1.0, 0.4)
	for bird in wildlife._birds:
		assert_false(bird.visible, "quiet shots do not receive incidental flybys")
	wildlife.set_film_cue(Wildlife.BIRD_CUE, 9.0)
	wildlife._update_small_life(1.0, 0.0, 0.4)
	for bird in wildlife._birds:
		assert_false(bird.visible, "rain suppresses the daytime birds")
	wildlife.free()


func test_cued_insects_stay_small_local_and_out_of_quiet_shots() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var wildlife := Wildlife.new(TerrainField.new())
	tree.root.add_child(wildlife)
	wildlife.set_process(false)
	wildlife._build_small_life()
	assert_eq(wildlife._gnats.multimesh.instance_count, 64, "eight habitat-local gnat clusters")
	assert_lt(Wildlife._butterfly_wing(1.0, 0).get_aabb().size.x * 2.0, 0.075, "butterfly wingspan stays below 75 mm")
	assert_lt(Wildlife._dragonfly_wings(1.0).get_aabb().size.x * 2.0, 0.09, "dragonfly wingspan stays below 90 mm")
	for seconds in 13:
		wildlife.set_film_cue(Wildlife.MEADOW_CUE, float(seconds))
		wildlife._update_small_life(1.0, 1.0, 0.4)
		for butterfly in wildlife._butterflies.slice(0, 2):
			assert_true(butterfly.visible, "sunlit meadow cue contains butterflies")
			assert_lt(butterfly.position.distance_to(wildlife._butterfly_anchor), 1.0, "butterflies remain inside the authored flower patch")
		for butterfly in wildlife._butterflies.slice(2):
			assert_false(butterfly.visible, "distributed habitat actors stay out of the close film cue")
		for dragonfly in wildlife._dragonflies:
			assert_false(dragonfly.visible, "pond insects do not accompany the meadow cue")
	wildlife.set_film_cue(&"", 3.0)
	wildlife._update_small_life(1.0, 1.0, 0.4)
	for butterfly in wildlife._butterflies:
		assert_false(butterfly.visible, "quiet film view has no added butterfly action")
	wildlife.set_film_cue(Wildlife.POND_CUE, 3.0)
	wildlife._update_small_life(1.0, 1.0, 0.4)
	assert_true(wildlife._dragonflies[0].visible, "pond view carries one close dragonfly")
	assert_false(wildlife._dragonflies[1].visible, "pond view stays free of a second competing insect")
	assert_gt(wildlife._dragonflies[0].position.y, TerrainField.WATER_LEVEL + 0.5, "dragonfly flight stays above water and lilies")
	wildlife._update_small_life(0.0, 1.0, 0.4)
	for dragonfly in wildlife._dragonflies:
		assert_false(dragonfly.visible, "day insects settle at night")
	wildlife.free()
