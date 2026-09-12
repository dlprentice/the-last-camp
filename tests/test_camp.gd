extends TestCase
## Placement and fire-intensity invariants for the authored camp.

const FirepitScript := preload("res://scripts/camp/firepit.gd")
const PropMeshesScript := preload("res://scripts/props/prop_meshes.gd")


func test_firepit_feed_clamps_and_raises() -> void:
	var field := TerrainField.new()
	var pit: Firepit = FirepitScript.new(field)
	pit.intensity = 1.0
	pit.feed()
	assert_gt(pit.intensity, 1.0, "feeding the fire should raise intensity")
	assert_lt(pit.intensity, Firepit.MAX_INTENSITY + 0.001, "intensity should stay at or under the cap")
	pit.intensity = Firepit.MAX_INTENSITY
	pit.feed()
	assert_near(pit.intensity, Firepit.MAX_INTENSITY, 0.001, "feeding a roaring fire should clamp")
	pit.free()


func test_camp_sits_above_the_water() -> void:
	var field := TerrainField.new()
	assert_gt(field.height(TerrainField.FIRE.x, TerrainField.FIRE.y), TerrainField.WATER_LEVEL + 0.4, "fire pad should be dry")
	assert_gt(field.height(TerrainField.TENT.x, TerrainField.TENT.y), TerrainField.WATER_LEVEL + 0.4, "tent pad should be dry")
	assert_lt(field.height(TerrainField.POND_CENTRE.x, TerrainField.POND_CENTRE.y), TerrainField.WATER_LEVEL, "pond basin should be underwater")


func test_shoreline_follows_the_authored_shape() -> void:
	var field := TerrainField.new()
	for i in 16:
		var angle := float(i) / 16.0 * TAU
		var r := TerrainField.pond_radius_at(angle)
		var inside := TerrainField.POND_CENTRE + Vector2(cos(angle), sin(angle)) * (r * 0.93)
		var outside := TerrainField.POND_CENTRE + Vector2(cos(angle), sin(angle)) * (r * 1.08)
		assert_lt(field.height(inside.x, inside.y), TerrainField.WATER_LEVEL, "just inside the shoreline is wet (angle %d)" % i)
		assert_gt(field.height(outside.x, outside.y), TerrainField.WATER_LEVEL, "just outside the shoreline is dry (angle %d)" % i)
	assert_gt(field.height(TerrainField.DOCK_START.x, TerrainField.DOCK_START.y), TerrainField.WATER_LEVEL, "dock starts on land")
	assert_lt(field.height(TerrainField.DOCK_START.x, TerrainField.DOCK_START.y), TerrainField.WATER_LEVEL + 0.6, "dock starts on the beach")


func test_dry_land_stays_above_water_across_the_meadow() -> void:
	var field := TerrainField.new()
	var rng := RandomNumberGenerator.new()
	rng.seed = 5
	for i in 400:
		var p := Vector2(rng.randf_range(-70.0, 70.0), rng.randf_range(-70.0, 70.0))
		var to_centre := p - TerrainField.POND_CENTRE
		if to_centre.length() < TerrainField.pond_radius_at(atan2(to_centre.y, to_centre.x)) * 1.2:
			continue
		assert_gt(field.height(p.x, p.y), TerrainField.WATER_LEVEL + 0.2, "meadow point %s is dry" % p)


func test_height_grid_matches_the_function() -> void:
	var field := TerrainField.new()
	field.bake_height_grid(20.0, 0.5)
	assert_near(field.height_fast(3.25, -7.75), field.height(3.25, -7.75), 0.03, "grid interpolates the function closely")
	assert_near(field.height_fast(0.0, 0.0), field.height(0.0, 0.0), 1e-4, "grid is exact on lattice points")
	assert_near(field.height_fast(50.0, 50.0), field.height(50.0, 50.0), 1e-6, "outside the grid falls back to the function")
	var axis := TerrainBuilder.axis_coordinates()
	assert_eq(axis[axis.size() / 2], 0.0, "axis is centred")
	assert_near(axis[axis.size() / 2 + 1], TerrainBuilder.INNER_SPACING, 1e-6, "inner spacing")
	assert_near(axis[axis.size() - 1], TerrainField.OUTER_EXTENT * 0.5, 1e-3, "axis reaches the world edge")


func test_distant_plants_sample_the_rendered_terrain_triangles() -> void:
	var field := TerrainField.new()
	field.bake_height_grid(TerrainBuilder.COLLISION_HALF, TerrainBuilder.INNER_SPACING)
	var builder := TerrainBuilder.new(field)
	var mesh := builder.build_mesh(null)
	var arrays := mesh.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	var axis := TerrainBuilder.axis_coordinates()
	for location in [Vector2(-95, 18), Vector2(-175, -36), Vector2(-265, 84),
			Vector2(-410, -130), Vector2(-630, 50), Vector2(590, 110)]:
		var ix := axis.bsearch(location.x) - 1
		var iz := axis.bsearch(location.y) - 1
		var start := (iz * (axis.size() - 1) + ix) * 6
		for triangle in 2:
			var a := vertices[indices[start + triangle * 3]]
			var b := vertices[indices[start + triangle * 3 + 1]]
			var c := vertices[indices[start + triangle * 3 + 2]]
			# Sample the mesh itself, independently of the field's grid formula.
			var point := a * 0.29 + b * 0.23 + c * 0.48
			assert_near(field.surface_height(point.x, point.z), point.y, 0.0001,
				"grass roots follow the actual rendered triangle at %s" % point)
	for i in range(axis.size() - 1):
		assert_lt(axis[i + 1] - axis[i], TerrainBuilder.OUTER_MAX_SPACING + 0.001,
			"outer cells remain small enough to support hill vegetation")


func test_scene_plan_keeps_the_clearing_open() -> void:
	var field := TerrainField.new()
	var plan := ScenePlan.new(field)
	plan.build()
	assert_gt(plan.trees.size(), 80.0, "the forest should be populated")
	assert_gt(plan.rocks.size(), 40.0, "rocks should be planned")
	assert_gt(plan.shrubs.size(), 20.0, "shrubs should be planned")
	for t in plan.near_trees():
		assert_gt(t.position.distance_to(TerrainField.FIRE), 8.5, "near trees must stay out of the fire circle")
		assert_gt(t.position.distance_to(TerrainField.TENT), 5.5, "near trees must stay off the tent pad")


func test_prop_meshes_are_nonempty() -> void:
	var log: ArrayMesh = PropMeshesScript.log_mesh(1.0, 0.1, 1)
	assert_gt(log.get_surface_count(), 0.0, "log mesh should have a surface")
	assert_gt(log.surface_get_array_len(0), 8.0, "log mesh should have vertices")
	var rock: ArrayMesh = PropMeshesScript.rock(3, 1.0)
	assert_gt(rock.surface_get_array_len(0), 20.0, "rock mesh should have vertices")
	var box: ArrayMesh = PropMeshesScript.box_mesh(Vector3.ONE)
	assert_eq(box.surface_get_array_len(0), 24, "a box is six faces of four verts")


func test_credit_roll_reads_every_source_table() -> void:
	var surfaces: Array = Cinematic._credit_rows("res://textures/SOURCES.md", 1, 2, 4)
	assert_eq(surfaces.size(), 9, "all nine surface sets are credited")
	assert_eq(surfaces[0][0], "Leafy Grass", "asset slugs read as names")
	assert_eq(surfaces[0][1], "Charlotte Baglioni", "CC0 creators carry no licence note")
	var models: Array = Cinematic._credit_rows("res://models/SOURCES.md", 1, 2, 4)
	assert_gt(models.size(), 30, "every photoscanned model is credited")
	var has_two_creators := false
	for row in models:
		if str(row[1]).find(",") != -1 and str(row[1]).find("(") == -1:
			has_two_creators = true
	assert_true(has_two_creators, "multiple creators are listed without their role notes")
	var sounds: Array = Cinematic._audio_credit_rows()
	assert_eq(sounds.size(), 6, "six recordings are credited once each")
	var by := false
	for row in sounds:
		if str(row[1]).find("SoundBible") != -1 and str(row[1]).find("CC BY 3.0") != -1:
			by = true
	assert_true(by, "attribution licences name the provider and the licence")


func test_ridges_plant_the_forest_species_to_a_closed_canopy() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var field := TerrainField.new()
	var plan := ScenePlan.new(field)
	plan.build()
	var forest := Forest.new(field, plan)
	tree.root.add_child(forest)
	forest.build()
	var ridges := RidgeForest.new(field, forest)
	tree.root.add_child(ridges)
	ridges.build()
	# 5.4 m spacing near, 6.6 m in the far band: about 21,000 trees with touching crowns.
	assert_gt(ridges.tree_count, 18000, "the hills carry a closed canopy, not a sparse scatter")
	assert_true(ridges.species_counts.has(TreeSpecies.Kind.OAK) and ridges.species_counts.has(TreeSpecies.Kind.SPRUCE),
		"ridges mix the forest's broadleaf and conifer species")
	assert_gt(forest.ridge_multimeshes.size(), 40, "ridge trees batch per species, variant and cell")
	var lod_levels := 0
	for mmi in forest.ridge_multimeshes:
		if mmi.name.begins_with("Ridge_bark") and mmi.multimesh.mesh.get_surface_count() > 0:
			var surface: Dictionary = RenderingServer.mesh_get_surface(mmi.multimesh.mesh.get_rid(), 0)
			lod_levels = maxi(lod_levels, (surface.get("lods", []) as Array).size())
	assert_gt(lod_levels, 0, "distant bark carries generated mesh LODs")
	ridges.free()
	forest.free()
func test_dock_and_canoe_build() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var field := TerrainField.new()
	field.bake_height_grid(60.0, 0.5)
	var holder := Node3D.new()
	tree.root.add_child(holder)
	var dock := Dock.new(field)
	holder.add_child(dock)
	dock.build()
	assert_true(dock.canoe != null, "dock moors a canoe")
	assert_true(dock.lantern != null, "dock carries a lantern")
	assert_gt(dock.get_node("Deck").mesh.surface_get_array_len(0), 500.0, "deck has many plank vertices")
	assert_gt(dock.get_node("Piles").mesh.surface_get_array_len(0), 100.0, "piles have vertices")
	assert_gt(dock.get_node("Mooring").mesh.surface_get_array_len(0), 20.0, "mooring line exists")
	assert_near(dock.deck_y, TerrainField.WATER_LEVEL + Dock.DECK_ABOVE_WATER, 1e-6, "deck sits above the water")
	var canoe: Canoe = dock.canoe
	assert_near(canoe.position.y, TerrainField.WATER_LEVEL - Canoe.DRAFT, 1e-6, "canoe floats at its draft")
	var to_centre := Vector2(canoe.position.x, canoe.position.z) - TerrainField.POND_CENTRE
	assert_lt(to_centre.length(), TerrainField.pond_radius_at(atan2(to_centre.y, to_centre.x)), "canoe is on the water")
	assert_gt(field.water_depth(canoe.position.x, canoe.position.z), Canoe.DRAFT + 0.2, "canoe has water under its keel")
	var hull: ArrayMesh = canoe.get_node("Hull").mesh
	assert_gt(hull.surface_get_array_len(0), 300.0, "hull is lofted")
	for top in dock._piles:
		var pile := dock.to_global(top)
		var gap := Vector2(pile.x, pile.z).distance_to(Vector2(canoe.position.x, canoe.position.z))
		assert_gt(gap, Canoe.HALF_BEAM + Dock.PILE_RADIUS + 0.3, "canoe clears pile at %s" % pile)
	assert_gt(Canoe.FLOOR_HEIGHT, Canoe.DRAFT, "canoe floor sits above the waterline")
	var deck_surface: TriangleMesh = dock.get_node("Deck").mesh.generate_triangle_mesh()
	var stones: Node3D = dock.get_node("SkippingStones")
	var stone_bounds: Array[AABB] = []
	for pebble: Node in stones.get_children():
		if not pebble is MeshInstance3D:
			continue
		var clearance := INF
		var bounds := AABB()
		var first := true
		for vertex: Vector3 in pebble.mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]:
			var p: Vector3 = stones.transform * pebble.transform * vertex
			bounds = AABB(p, Vector3.ZERO) if first else bounds.expand(p)
			first = false
			var hit := deck_surface.intersect_ray(Vector3(p.x, 0.25, p.z), Vector3.DOWN)
			if not hit.is_empty():
				clearance = minf(clearance, p.y - hit.position.y)
		assert_gt(clearance, -0.0001, "a skipping stone does not penetrate a plank")
		assert_lt(clearance, 0.0005, "every skipping stone touches an actual plank")
		for other in stone_bounds:
			assert_false(bounds.intersects(other), "the loose stones do not overlap")
		stone_bounds.append(bounds)
	assert_eq(stone_bounds.size(), 5, "all five skipping stones remain")
	holder.free()


func test_lantern_posts_stand_beside_the_trail() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var field := TerrainField.new()
	field.bake_height_grid(60.0, 0.5)
	var holder := Node3D.new()
	tree.root.add_child(holder)
	var site := Campsite.new(field)
	holder.add_child(site)
	site.build()
	var posted := 0
	for lantern in site.lanterns:
		if lantern.get_node_or_null("Post") == null:
			continue
		posted += 1
		var p := lantern.global_position
		assert_gt(field.trail_distance(p.x, p.z), 1.4, "post at %s stands off the trail" % p)
		assert_gt(Vector2(p.x, p.z).distance_to(TerrainField.FIRE), 3.0, "post keeps clear of the fire circle")
	assert_eq(posted, 2, "two hook posts are placed")
	holder.free()


func test_intro_dolly_path_is_clear_of_trees() -> void:
	var field := TerrainField.new()
	var plan := ScenePlan.new(field)
	plan.build()
	var hits := IntroDolly.obstructions(field, plan)
	assert_eq(hits.size(), 0, "dolly path is obstructed: %s" % ", ".join(hits.slice(0, 3)))


func test_cinematic_shots_are_clear_of_props_and_trees() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var field := TerrainField.new()
	field.bake_height_grid(60.0, 0.5)
	var plan := ScenePlan.new(field)
	plan.build()
	var holder := Node3D.new()
	tree.root.add_child(holder)
	var dock := Dock.new(field)
	holder.add_child(dock)
	dock.build()
	var piles: Array[Vector3] = []
	for top in dock._piles:
		piles.append(dock.to_global(top))
	var hits := Cinematic.obstructions(field, plan, "showreel", piles, dock.canoe.position, dock.canoe.rotation.y)
	hits.append_array(Cinematic.obstructions(field, plan, "afterglow", piles, dock.canoe.position, dock.canoe.rotation.y))
	hits.append_array(Cinematic.obstructions(field, plan, "one_night", piles, dock.canoe.position, dock.canoe.rotation.y))
	assert_eq(hits.size(), 0, "cinematic shots obstructed: %s" % ", ".join(hits.slice(0, 5)))
	holder.free()


func test_player_item_and_lantern_state() -> void:
	var player := Player.new()
	assert_eq(player.held_item, &"", "player starts empty-handed")
	player.held_item = &"log"
	assert_eq(player.held_item, &"log", "held item should stick")
	assert_false(player.lantern_on, "hand lantern starts off")
	player.free()


func test_meadow_cover_and_camp_clearance() -> void:
	var field := TerrainField.new()
	assert_gt(field.grass_suitability(9, 8), 0.7, "open meadow supports a continuous grass stand")
	assert_near(field.grass_suitability(0, 0), 0, 0.001, "fire pad stays bare")
	assert_near(field.grass_suitability(7.5, -4.5), 0, 0.001, "tent stays clear of grass")
	assert_near(field.grass_suitability(-34, 4), 0, 0.001, "grass cannot grow through the pond")
	assert_lt(field.grass_suitability(1.0, 9.0), 0.1, "walked trail stays legible")


func test_lilies_live_in_shallows_outside_the_dock_lane() -> void:
	var field := TerrainField.new()
	var shore := ShoreLife.new(field)
	shore.build()
	assert_gt(shore.pad_positions.size(), 30, "sheltered banks carry lily colonies")
	for p in shore.pad_positions:
		assert_gt(field.water_depth(p.x, p.z), 0.2, "every lily is over water")
		assert_lt(field.water_depth(p.x, p.z), 2.4, "lilies only grow in the shallows")
		assert_near(p.y, TerrainField.WATER_LEVEL + 0.01, 0.01, "pads stay within two centimetres of the water")
		var stem := shore._stem_transform(p)
		assert_near((stem * Vector3.ZERO).distance_to(p), 0.0, 1e-6, "a leaning petiole stays attached to its leaf")
		var root := stem * Vector3(0.08, -1.0, -0.06)
		assert_near(root.y, field.height(root.x, root.z) - 0.012, 0.0001, "rhizome roots meet the bed after bending")
		var lane: Array[Vector2] = [TerrainField.DOCK_START, TerrainField.POND_CENTRE]
		assert_gt(TerrainField.distance_to_polyline(Vector2(p.x, p.z), lane), 3.0, "the dock approach remains open")
	shore.free()


func test_stone_skips_finish_and_leave_ripples() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var pond := Pond.new(TerrainField.new())
	tree.root.add_child(pond)
	pond.build()
	assert_false(pond.skip_stone(Vector3(10, 1, 10), Vector3.RIGHT), "dry ground rejects a skip")
	assert_true(pond.skip_stone(Vector3(-25, 0, 4), Vector3.LEFT), "open water accepts a skip")
	assert_false(pond.skip_stone(Vector3(-25, 0, 4), Vector3.LEFT), "a running skip rejects repeated input")
	for i in 180:
		pond._ripple_clock += 1.0 / 60.0
		pond._update_skip(1.0 / 60.0)
	assert_eq(pond._ripple_cursor, 3, "three surface contacts leave three impulses")
	assert_false(pond._stone.visible, "stone disappears after the final impact")
	assert_lt(pond._skip_age, 0.0, "skip releases its input lock")
	pond.free()


func test_kitchen_props_and_feet_have_support() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var field := TerrainField.new()
	var kitchen := CampKitchen.new(field)
	tree.root.add_child(kitchen)
	kitchen.build()
	assert_eq(kitchen.feet.size(), 4, "the table has four measured feet")
	for foot in kitchen.feet:
		assert_near(foot.y, field.height(foot.x, foot.z) - 0.025, 0.002, "every foot meets the ground")
	for node_name in ["CoffeePot", "EnamelMug", "FieldJournal"]:
		assert_near(kitchen.get_node(node_name).position.y, CampKitchen.CLOTH_TOP, 0.001, "cloth is the mount for %s" % node_name)
	assert_lt(field.grass_suitability(TerrainField.TABLE.x, TerrainField.TABLE.y), 0.01, "the kitchen has a clear working pad")
	var support: BoxShape3D = kitchen.get_node("ClothSupport").get_child(0).shape
	assert_lt(support.size.y, 0.05, "cloth collides with the thin tabletop, not the rounded player blocker")
	assert_near(CampKitchen._linen_crease(-0.36, -0.02), 0, 1e-6, "kettle has a flat fabric footprint")
	kitchen.free()


func test_cloth_has_connected_faces_and_noncollapsed_corners() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var kitchen := CampKitchen.new(TerrainField.new())
	tree.root.add_child(kitchen)
	kitchen.build()
	var arrays := kitchen.cloth.mesh.surface_get_arrays(0)
	var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	var directed_edges := {}
	for triangle in range(0, indices.size(), 3):
		var a := indices[triangle]
		var b := indices[triangle + 1]
		var c := indices[triangle + 2]
		assert_gt((verts[b] - verts[a]).cross(verts[c] - verts[a]).length(), 1e-7, "no cloth triangle collapses into a line")
		for edge: Vector2i in [Vector2i(a, b), Vector2i(b, c), Vector2i(c, a)]:
			assert_false(directed_edges.has(edge), "adjacent cloth faces must wind consistently")
			directed_edges[edge] = true
	kitchen.free()


func test_film_demonstrates_live_changes_and_a_continuous_water_crossing() -> void:
	var shots := Cinematic.sequence("one_night")
	var lapses := 0
	var feed_cues := 0
	for shot in shots:
		if not is_nan(shot.hour_end) and absf(shot.hour_end - shot.hour) > 1.0:
			lapses += 1
		if shot.feed_fire and shot.feed_at > 0.0:
			feed_cues += 1
	assert_eq(lapses, 2, "sunset and sunrise carry the major visible time transitions")
	var dawn: Cinematic.Shot
	for shot in shots:
		if shot.weather == "dawn":
			dawn = shot
	assert_true(dawn != null, "the film contains dawn")
	assert_lt(dawn.hour, 29.75, "dawn opens before sunrise")
	assert_gt(dawn.hour_end, 29.75, "the first rays arrive on screen")
	assert_gt(feed_cues, 0, "a visible fire shot contains a timed feed response")
	assert_near(Cinematic.duration("one_night"), 337.0, 1e-6, "five minutes of film plus the title, closing card and credit roll")
	var crossing: Cinematic.Shot
	for shot in shots:
		if shot.label == "Beneath the reflections":
			crossing = shot
	assert_true(crossing != null, "one uninterrupted shot contains the water crossing")
	assert_gt(crossing.path.front().y, TerrainField.WATER_LEVEL + 0.2, "crossing begins in air")
	assert_gt(crossing.path.back().y, TerrainField.WATER_LEVEL + 0.2, "crossing returns to air")
	assert_lt(crossing.path[3].y, TerrainField.WATER_LEVEL - 0.5, "crossing visits the pond bed")
	assert_near(Spline.new(crossing.path).sample(crossing.rest_at).distance_to(crossing.path[3]),
		0.0, 1e-5, "the underwater hold stays at its composed viewpoint when the exit path changes")
	for i in shots.size():
		assert_true(shots[i].clock, "every scene shows the world clock")
		if shots[i].continuous_in:
			assert_gt(i, 0, "a continuous move has a preceding shot")
			var previous := shots[i - 1]
			assert_eq(previous.fade_out, 0.0, "a continuous move stays visible")
			assert_eq(shots[i].fade_in, 0.0, "a continuous move has no dissolve")
			var field := TerrainField.new()
			assert_near(Cinematic.resolve(field, previous.path, previous.absolute).back().distance_to(
				Cinematic.resolve(field, shots[i].path, shots[i].absolute).front()), 0, 1e-5, "no camera cut")
			assert_near(Cinematic.resolve(field, previous.look, previous.absolute).back().distance_to(
				Cinematic.resolve(field, shots[i].look, shots[i].absolute).front()), 0, 1e-5, "no aim cut")
			assert_near(previous.fov_end if not is_nan(previous.fov_end) else previous.fov,
				shots[i].fov, 1e-5, "no lens cut")
			assert_near(previous.exposure_end if not is_nan(previous.exposure_end) else previous.exposure,
				shots[i].exposure, 1e-5, "no exposure cut")
		else:
			assert_gt(shots[i].fade_in, 0.5, "a new viewpoint must fade in")
		if i == shots.size() - 1 or not shots[i + 1].continuous_in:
			assert_gt(shots[i].fade_out, 0.5, "every separate viewpoint fades out")
		assert_gt(shots[i].hour_end, shots[i].hour, "time progresses through every scene")
		if i > 0:
			if shots[i].weather == "dawn":
				assert_gt(shots[i].hour, shots[i-1].hour_end, "the overnight time jump moves forward under a fade")
			else:
				assert_near(shots[i].hour, shots[i-1].hour_end, 1e-6, "the clock stays continuous within each part of the evening")
	assert_eq(Cinematic.clock_text(24.0), "00:00", "midnight wraps cleanly")
	assert_eq(Cinematic.clock_text(28.7), "04:42", "unwrapped dawn time displays normally")


func test_film_camera_moves_slowly_without_speed_steps() -> void:
	var field := TerrainField.new()
	for shot in Cinematic.one_night():
		var path := Spline.new(Cinematic.resolve(field, shot.path, shot.absolute))
		var look := Spline.new(Cinematic.resolve(field, shot.look, shot.absolute))
		var previous := Cinematic.camera_position(shot, path, field, 0.0)
		var direction := (look.sample(0) - previous).normalized()
		var max_speed := 0.0
		var max_turn := 0.0
		for frame in range(1, roundi(shot.duration * 60.0) + 1):
			var u := Cinematic.motion_progress(shot, frame / 60.0)
			var p := Cinematic.camera_position(shot, path, field, frame / 60.0)
			var next_direction := (Cinematic.aim_target(shot, p, look.sample(u), frame / 60.0) - p).normalized()
			max_speed = maxf(max_speed, p.distance_to(previous) * 60.0)
			max_turn = maxf(max_turn, rad_to_deg(direction.angle_to(next_direction)) * 60.0)
			previous = p
			direction = next_direction
		var speed_limit := 1.4 if shot.walk else 0.55
		assert_lt(max_speed, speed_limit, "%s camera respects the walking/observation pace (%.3f m/s)" % [shot.label, max_speed])
		assert_lt(max_turn, 8.0 if shot.walk or not shot.pitch_envelope.is_empty() else 5.5, "%s has a gentle turn (%.3f deg/s)" % [shot.label, max_turn])
		assert_near(Cinematic.motion_progress(shot, shot.hold_start), 0.0, 1e-6, "opening composition is held")
		assert_near(Cinematic.motion_progress(shot, shot.duration - shot.hold_end), 1.0, 1e-6, "closing composition is held")
	var uneven := Spline.new([Vector3.ZERO, Vector3(0.8, 1.5, 0), Vector3(10, 0, 2), Vector3(12, 3, 4)])
	var expected := uneven.total_length() / 1000.0
	for i in range(1, 1000):
		var distance := uneven.sample(i / 1000.0).distance_to(uneven.sample((i - 1) / 1000.0))
		assert_near(distance, expected, expected * 0.06, "arc-length sampling avoids speed changes between unequal segments")


func test_camper_camera_follows_ground_and_stops_its_gait() -> void:
	var field := TerrainField.new()
	var walking_shots := 0
	for shot in Cinematic.one_night():
		if not shot.walk:
			continue
		walking_shots += 1
		var path := Spline.new(Cinematic.resolve(field, shot.path, shot.absolute))
		for frame in range(0, roundi(shot.duration * 60.0) + 1, 5):
			var seconds := frame / 60.0
			var pos := Cinematic.camera_position(shot, path, field, seconds)
			var height := pos.y - Cinematic.walking_support(field, pos)
			assert_near(height, shot.eye_height, 0.025, "eye follows terrain/deck rather than floating between spline control points")
		var start := Cinematic.camera_position(shot, path, field, 0.0)
		assert_near(start.distance_to(Cinematic.camera_position(shot, path, field, shot.hold_start * 0.5)), 0.0, 1e-6, "no gait while waiting to walk")
	assert_eq(walking_shots, 4, "arrival, pond approach, night return and dawn are grounded walks")
	var deck_pos := Vector3(-18.5, 0, 5.97)
	assert_true(Cinematic.on_dock(deck_pos), "the pond walk actually reaches the boards")
	assert_near(Cinematic.walking_support(field, deck_pos), TerrainField.WATER_LEVEL + Dock.DECK_ABOVE_WATER, 0.01, "deck height supports the walker above the water")


func test_fire_ring_is_partially_embedded_across_each_footprint() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var field := TerrainField.new()
	var pit := Firepit.new(field)
	tree.root.add_child(pit)
	pit.position = Vector3(TerrainField.FIRE.x, field.height(0, 0), TerrainField.FIRE.y)
	pit._build_ring()
	assert_eq(pit.get_child_count(), 15, "the complete stone ring remains")
	for stone: MeshInstance3D in pit.get_children():
		var low := INF
		var high := -INF
		for vertex: Vector3 in stone.mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]:
			var p := stone.to_global(vertex)
			var clearance := p.y - field.height(p.x, p.z)
			low = minf(low, clearance)
			high = maxf(high, clearance)
		assert_lt(low, -0.025, "the base enters the soil")
		assert_gt(high, 0.08, "the stone still has a visible crown")
		assert_gt(-low / (high - low), 0.27, "lower stone mass is embedded")
		assert_lt(-low / (high - low), 0.37, "the ring is not buried out of sight")
	pit.free()


func test_seats_face_the_fire_and_leave_the_paths_open() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var field := TerrainField.new()
	var site := Campsite.new(field)
	tree.root.add_child(site)
	site._build_seats()
	for i in TerrainField.SEATS.size():
		var bench: Node3D = site.get_node("SplitLogBench_%d" % i)
		var centre := TerrainField.SEATS[i]
		var inward := Vector3(-centre.x, 0, -centre.y).normalized()
		assert_gt((-bench.basis.z).dot(inward), 0.999, "the sitting side faces the hearth")
		for x: float in [-0.925, 0.0, 0.925]:
			for z: float in [-0.20, 0.20]:
				var p := bench.to_global(Vector3(x, 0, z))
				assert_gt(field.walking_distance(Vector2(p.x, p.z)), 0.80, "seating keeps the walked routes clear")
				assert_gt(TerrainField.camp_wear(Vector2(p.x, p.z)), 0.94, "grass cannot grow through the seat")
		var footwell := centre - centre.normalized() * 0.32
		assert_gt(TerrainField.camp_wear(footwell), 0.9, "feet have a worn patch of earth")
	site.free()


func test_water_fog_waits_for_the_whole_near_plane() -> void:
	var tan_half := tan(deg_to_rad(62.0) * 0.5)
	for basis in [Basis.IDENTITY, Basis.from_euler(Vector3(0.35, 0, 0.4))]:
		var frame := Transform3D(basis, Vector3(0, TerrainField.WATER_LEVEL, 0))
		assert_near(WorldController.water_fog_blend(frame, 0.05, tan_half, 16.0 / 9.0), 0.0, 1e-6, "global fog must not obscure the above-water part of the lens")
		frame.origin.y -= 0.25
		assert_near(WorldController.water_fog_blend(frame, 0.05, tan_half, 16.0 / 9.0), 1.0, 1e-6, "deeply submerged lens uses native fog")
	# Include the bounded interaction waves as well as the analytic wind swell.
	var crossing := Transform3D(Basis.IDENTITY, Vector3(0, TerrainField.WATER_LEVEL - WorldController.WATER_WAVE_ENVELOPE - 0.055, 0))
	var blend := WorldController.water_fog_blend(crossing, 0.05, tan_half, 16.0 / 9.0)
	assert_gt(blend, 0.0, "fog eases in after the lens clears the waves")
	assert_lt(blend, 1.0, "the handoff covers a range of depths")


func test_water_lens_crossings_hysteresis_and_drying() -> void:
	var world := WorldController.new()
	world.update_water_lens(-0.3, 0.1)
	assert_eq(world.lens_entry_age, 100.0, "opening pose does not invent a splash")
	world.update_water_lens(0.10, 0.1)
	assert_eq(world.lens_entry_age, 0.0, "diving starts one bubble burst")
	world.update_water_lens(0.01, 0.5)
	world.update_water_lens(-0.02, 0.5)
	assert_near(world.lens_entry_age, 1.0, 1e-6, "small waves do not repeat the entry")
	assert_eq(world.lens_exit_age, 100.0, "small waves do not fabricate an exit")
	world.update_water_lens(-0.10, 0.1)
	assert_eq(world.lens_exit_age, 0.0, "surfacing starts the draining film")
	world.update_water_lens(-0.30, 3.0)
	assert_near(world.lens_exit_age, 3.0, 1e-6, "runoff keeps drying above water")
	world.reset_water_lens()
	world.update_water_lens(0.8, 0.1)
	assert_eq(world.lens_entry_age, 100.0, "a capture teleport starts with a settled lens")
	world.free()


func test_meadow_planning_keeps_walked_routes_bare() -> void:
	var field := TerrainField.new()
	field.bake_height_grid(16.0, 0.5)
	var planter := GrassPlanter.new(field, 16.0, 0.2)
	planter.plan()
	for chunk in planter.chunks:
		for i in chunk.count:
			var index := i * GrassPlanter.FLOATS_PER_INSTANCE
			var p := Vector2(chunk.buffer[index + 3], chunk.buffer[index + 11])
			assert_gt(field.walking_distance(p), 0.519, "grass must leave a clear walking ribbon")
			assert_lt(TerrainField.camp_wear(p), 0.941, "dense grass must stay out of the working pads")


func test_reflection_culling_preserves_water_crossing_the_view_edge() -> void:
	var planes: Array[Plane] = [Plane(Vector3.RIGHT, 1.0)]
	assert_true(Pond.bounds_in_frustum(AABB(Vector3(0.5, 0, 0), Vector3.ONE), planes), "a partially visible surface must still render")
	assert_true(Pond.bounds_in_frustum(AABB(Vector3(-3, 0, 0), Vector3.ONE), planes), "water inside the plane remains visible")
	assert_true(not Pond.bounds_in_frustum(AABB(Vector3(1.01, 0, 0), Vector3.ONE), planes), "a completely excluded surface can suspend the mirror")


func test_waterline_effect_is_excluded_from_the_mirror() -> void:
	var world := WorldController.new()
	world._build_post()
	world._build_water_volume()
	var pond := Pond.new(TerrainField.new())
	pond.material = ShaderMaterial.new()
	pond._build_reflection()
	assert_eq(world.waterline.layers & pond.reflection_camera.cull_mask, 0,
		"the mirrored camera must not bake its underwater lens fog into the pond reflection")
	assert_eq(world.waterline.layers & pond.underwater_camera.cull_mask, 0,
		"the underside mirror must also exclude the screen waterline and surface")
	for camera in [pond.reflection_camera, pond.underwater_camera, pond.transmission_camera]:
		assert_eq(world.water_volume.layers & camera.cull_mask, 0,
			"crossing light scattering must not enter an auxiliary water view")
	assert_eq(pond.reflection_camera.cull_mask & Pond.UNDERWATER_REFLECTION_LAYER, 0,
		"the upper mirror must preserve the air halfspace")
	assert_gt(pond.underwater_camera.cull_mask & Pond.UNDERWATER_REFLECTION_LAYER, 0,
		"the underside mirror must preserve the submerged halfspace")
	assert_eq(pond.underwater_camera.cull_mask & Pond.AIR_FOG_LAYER, 0,
		"the reflected water must not integrate air mist below its physical halfspace")
	var reflected_volume: FogVolume = pond.get_node("ReflectedWaterVolume")
	assert_gt(reflected_volume.layers & pond.underwater_camera.cull_mask, 0,
		"the underside reflection must include its own bounded light scattering")
	for camera in [pond.reflection_camera, pond.transmission_camera]:
		assert_eq(reflected_volume.layers & camera.cull_mask, 0,
			"reflected water scattering must not enter an air view")
	var mirror_fog: MeshInstance3D = pond.get_node("ReflectedWaterPath")
	assert_eq(mirror_fog.layers & pond.reflection_camera.cull_mask, 0,
		"reflected-path fog must never affect the upper mirror")
	assert_eq(mirror_fog.layers & pond.transmission_camera.cull_mask, 0,
		"reflected-path fog must never affect air transmission")
	world.free()
	pond.free()


func test_smooth_normals_retain_the_authored_outward_direction() -> void:
	var mb := MeshBuilder.new()
	var a := mb.add_vertex(Vector3.ZERO, Vector3.UP, Vector2.ZERO)
	var b := mb.add_vertex(Vector3.RIGHT, Vector3.UP, Vector2.RIGHT)
	var c := mb.add_vertex(Vector3(1, 0, 1), Vector3.UP, Vector2.ONE)
	var d := mb.add_vertex(Vector3.BACK, Vector3.UP, Vector2.DOWN)
	mb.add_quad_facing(a, b, c, d, Vector3.UP)
	mb.recompute_normals()
	for normal in mb.normals:
		assert_gt(normal.dot(Vector3.UP), 0.99, "clockwise faces must retain upward lighting normals")


func test_grass_lods_keep_every_blade_root_and_tip() -> void:
	for rows in [PackedInt32Array([0, 2, 4]), PackedInt32Array([0, 3]), PackedInt32Array([0])]:
		var indices := GrassPlanter.clump_lod_indices(7, 5, rows)
		assert_lt(indices.size(), 7 * 11 * 3, "each level reduces geometry")
		for blade in 7:
			assert_true(indices.has(blade * 13), "each blade retains its root")
			assert_true(indices.has(blade * 13 + 12), "each blade retains its full height")


func test_surfacing_restores_the_sky_and_air_fog() -> void:
	var world := WorldController.new()
	world.environment = Environment.new()
	world._build_post()
	for blend: float in [0.0, 0.25, 0.75, 1.0]:
		world.underwater_blend = blend
		world._apply_fog()
		var partial: float = world.waterline_material.get_shader_parameter("density")
		assert_near(world.environment.fog_density + partial, WorldController.WATER_FOG_DENSITY, 1e-6,
			"the lens handoff must not add or lose water density")
	world.underwater = true
	world.underwater_blend = 1.0
	world._apply_fog()
	assert_gt(world.environment.fog_density, WorldController.AIR_FOG_DENSITY * 10.0, "water remains denser than clear air")
	assert_near(world.environment.ambient_light_energy, 0.6, 1e-6, "water receives subdued ambient light")
	world.underwater = false
	world.underwater_blend = 0.0
	world._apply_fog()
	assert_eq(world.environment.background_color, Color.BLACK, "water colour must not remain behind the sky")
	assert_lt(world.environment.fog_density, 0.001, "surfacing restores clear air")
	assert_lt(world.environment.volumetric_fog_density, 0.001, "the water volume must not follow the camera onto land")
	world.free()


func test_storm_cues_keep_flashes_brief_and_ground_wet() -> void:
	var before := CampWeather.conditions("storm", 2.0, 40.0)
	var first := CampWeather.conditions("storm", 9.0, 40.0)
	var strike := CampWeather.conditions("storm", 33.0, 40.0)
	var between := CampWeather.conditions("storm", 12.0, 40.0)
	assert_eq(before.y, 0.0, "the rain arrives after the first distant thunderhead")
	assert_gt(first.w, 0.5, "first flash exists")
	assert_gt(strike.w, first.w, "the closer strike is stronger")
	assert_eq(between.w, 0.0, "darkness separates strikes")
	assert_gt(CampWeather.conditions("storm_short", 16.5, 20.0).w, 0.9, "the short edit retains the main lightning event")
	assert_gt(CampWeather.conditions("storm_short", 18.0, 20.0).z, 0.99, "surfaces are wet before the close rain shot")
	assert_eq(CampWeather.strike_flash(-0.01), 0.0, "no light precedes a strike")
	assert_eq(CampWeather.strike_flash(0.6), 0.0, "no lingering lightning")
	assert_eq(CampWeather.conditions("shelter_rain", 2.0, 30.0).y, 1.0, "rain continues outside the shelter")
	var clearing := CampWeather.conditions("clearing", 5.0, 24.0)
	assert_eq(clearing.y, 0.0, "rain stops for the clearing stars")
	assert_eq(clearing.z, 1.0, "surfaces stay wet after rainfall")
	assert_gt(CampWeather.conditions("dawn", 20.0, 20.0).z, 0.25, "dawn does not instantly dry the camp")
	var weather := CampWeather.new()
	for seconds in [8.0, 12.0, 16.0, 20.0]:
		weather.apply_chapter("clearing", seconds, 24.0, false)
		assert_eq(weather.cloud_coverage(0.0), 0.0, "advected cumulus cannot hide the star reveal")
	weather.apply_chapter("", 0.0, 1.0, false)
	assert_eq(weather.cloud_coverage(1.0), 0.36, "the approved daylight cloud cover stays unchanged")
	weather.free()
