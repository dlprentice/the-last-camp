extends TestCase
## Structural contracts for the ecological/art-direction passes.


func test_main_scene_wires_biome_layers() -> void:
	var packed := load("res://scenes/main.tscn") as PackedScene
	assert_true(packed != null, "main scene loads with biome scripts")
	if packed == null:
		return
	var scene := packed.instantiate()
	assert_true(scene.get_node_or_null("BiomeDressing") != null, "moisture-zoned biome dressing is wired")
	assert_true(scene.get_node_or_null("ForestFloorDressing") != null, "forest-floor dressing is wired")
	scene.free()


func test_mature_tree_geometry_exceeds_saplings() -> void:
	var oak := TreeSpecies.oak()
	var alder := TreeSpecies.alder()
	var spruce := TreeSpecies.spruce()
	var pine := TreeSpecies.pine()
	var sapling := TreeSpecies.sapling()
	assert_gt(oak.trunk_segments, sapling.trunk_segments, "mature oak trunks use higher radial/vertical detail")
	assert_gt(pine.trunk_segments, sapling.trunk_segments, "mature pine trunks use higher detail")
	assert_gt(oak.root_flare, sapling.root_flare, "mature oak has stronger root flare")
	assert_gt(alder.root_flare, sapling.root_flare, "wet-bank alder has stronger root flare")
	assert_gt(spruce.height.y, oak.height.y, "spruce retains its tall conifer silhouette")
	assert_gt(pine.height.y, spruce.height.y, "pine remains the tallest species envelope")


func test_growth_habits_are_materially_distinct() -> void:
	var low_fork := TreeSpecies.variant(TreeSpecies.Kind.OAK, 0)
	var high_fork := TreeSpecies.variant(TreeSpecies.Kind.OAK, 5)
	assert_lt(low_fork.split_height, high_fork.split_height, "oak variants span low- and high-fork habits")
	assert_gt(low_fork.split_angle, high_fork.split_angle, "low-fork oak spreads more broadly")
	var wind_pine := TreeSpecies.variant(TreeSpecies.Kind.PINE, 3)
	var upright_pine := TreeSpecies.variant(TreeSpecies.Kind.PINE, 4)
	assert_gt(wind_pine.trunk_lean, upright_pine.trunk_lean * 2.0, "pine variants include visibly wind-shaped and upright habits")


func test_floor_detail_meshes_are_real_geometry() -> void:
	var root := PropMeshes.log_mesh(1.0, 0.085, 9400, 0.12)
	var deadwood := PropMeshes.bark_log_mesh(1.0, 0.13, 9600, 0.13)
	var stone := PropMeshes.rock(9800, 1.0)
	assert_gt(root.surface_get_array_len(0), 20, "buttress-root mesh has real cylindrical geometry")
	assert_gt(deadwood.get_surface_count(), 1, "fallen log carries bark plus end geometry")
	assert_gt(stone.surface_get_array_len(0), 20, "moss-stone source has real geometry")


func test_shore_distance_has_expected_sign() -> void:
	var east_angle := 0.0
	var shore := TerrainField.shore_point(east_angle)
	var radial := (shore - TerrainField.POND_CENTRE).normalized()
	assert_lt(BiomeDressing._shore_offset(shore - radial * 0.5), 0.0, "inside sample is water-side of shore")
	assert_gt(BiomeDressing._shore_offset(shore + radial * 0.5), 0.0, "outside sample is land-side of shore")
