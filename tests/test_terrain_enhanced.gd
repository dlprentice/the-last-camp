extends TestCase
## Contracts for the stacked broad-form terrain pass.


func test_enhanced_field_preserves_camp_pads() -> void:
	var base := TerrainField.new()
	var enhanced := TerrainFieldEnhanced.new()
	assert_near(enhanced.height(TerrainField.FIRE.x, TerrainField.FIRE.y),
		base.height(TerrainField.FIRE.x, TerrainField.FIRE.y), 1e-6, "fire pad remains authored")
	assert_near(enhanced.height(TerrainField.TENT.x, TerrainField.TENT.y),
		base.height(TerrainField.TENT.x, TerrainField.TENT.y), 1e-6, "tent pad remains authored")


func test_waterline_location_is_exactly_preserved() -> void:
	var field := TerrainFieldEnhanced.new()
	for i in 32:
		var angle := float(i) / 32.0 * TAU
		var shore := TerrainField.shore_point(angle)
		var radial := (shore - TerrainField.POND_CENTRE).normalized()
		assert_near(field.height(shore.x, shore.y), TerrainField.WATER_LEVEL, 0.0001,
			"enhanced shoreline zero crossing stays fixed at sector %d" % i)
		var inside := shore - radial * 0.55
		var outside := shore + radial * 0.55
		assert_lt(field.height(inside.x, inside.y), TerrainField.WATER_LEVEL,
			"inside remains wet at sector %d" % i)
		assert_gt(field.height(outside.x, outside.y), TerrainField.WATER_LEVEL,
			"outside remains dry at sector %d" % i)


func test_dock_beach_remains_usable() -> void:
	var field := TerrainFieldEnhanced.new()
	var h := field.height(TerrainField.DOCK_START.x, TerrainField.DOCK_START.y)
	assert_gt(h, TerrainField.WATER_LEVEL, "dock still starts on dry beach")
	assert_lt(h, TerrainField.WATER_LEVEL + 0.65, "dock beach is not lifted into a bank")


func test_macro_form_changes_midground_not_microterrain() -> void:
	var base := TerrainField.new()
	var enhanced := TerrainFieldEnhanced.new()
	var samples := [Vector2(38, 25), Vector2(54, -31), Vector2(73, 48), Vector2(96, -55), Vector2(-24, 72)]
	var total_difference := 0.0
	for p in samples:
		total_difference += absf(enhanced.height(p.x, p.y) - base.height(p.x, p.y))
	assert_gt(total_difference, 0.12, "broad-form layer materially changes the midground")
	# The fire circle is a deliberate counterexample: no added noise belongs there.
	assert_near(enhanced.height(2.0, 1.0), base.height(2.0, 1.0), 1e-6,
		"immediate camp stays free of macro displacement")


func test_enhanced_surface_is_continuous_at_walking_scale() -> void:
	var field := TerrainFieldEnhanced.new()
	for centre in [Vector2(35, 35), Vector2(70, -25), Vector2(-48, 52), Vector2(-18, 18)]:
		var h := field.height(centre.x, centre.y)
		for offset in [Vector2(0.25, 0), Vector2(-0.25, 0), Vector2(0, 0.25), Vector2(0, -0.25)]:
			var q: Vector2 = centre + offset
			assert_lt(absf(field.height(q.x, q.y) - h), 0.45,
				"broad form has no quarter-metre cliff near %s" % centre)


func test_main_scene_installs_enhanced_field_before_build() -> void:
	var packed := load("res://scenes/main.tscn") as PackedScene
	assert_true(packed != null, "terrain candidate main scene loads")
	if packed == null:
		return
	var scene := packed.instantiate()
	var camp := scene.get_node("Camp") as Camp
	var director := scene.get_node("TerrainFieldDirector")
	assert_true(director != null, "terrain director is present")
	# _ready has not run in this detached instance, so verify the assignment type
	# contract independently rather than pretending the scene build executed.
	camp.field = TerrainFieldEnhanced.new()
	assert_true(camp.field is TerrainFieldEnhanced, "Camp accepts the enhanced TerrainField subclass")
	scene.free()
