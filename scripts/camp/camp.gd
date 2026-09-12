class_name Camp
extends Node3D

## Builds the authored scene: survey, landscape, forest, understory, water,
## the camp itself and its wildlife. Pure generation (terrain meshing, grass
## planning) runs on worker threads while the main thread assembles nodes and
## keeps the loading screen alive.

signal stage_started(stage_name: String, index: int, total: int)

const STAGE_COUNT := 7

var field := TerrainField.new()
var plan: ScenePlan
var terrain_material: ShaderMaterial
var terrain: MeshInstance3D
var terrain_body: StaticBody3D
var forest: Forest
var ridge_forest: RidgeForest
var understory: Understory
var pond: Pond
var underwater_fx: UnderwaterFX
var campsite: Campsite
var rocks: CampRocks
var wildlife: Wildlife
var audio: AudioDirector
var shore_life: ShoreLife

var _stage_index := 0
var _stage_name := ""
var _stage_started_ms := 0
var _terrain_thread: Thread
var _grass_thread: Thread


func _ready() -> void:
	Game.camp = self
	RenderingServer.global_shader_parameter_set("water_level", TerrainField.WATER_LEVEL)
	RenderingServer.global_shader_parameter_set("fire_position", Vector3(TerrainField.FIRE.x, 0.55, TerrainField.FIRE.y))


## Worker threads must be joined before the engine tears the scene down, even
## when the window is closed halfway through loading.
func _exit_tree() -> void:
	for thread in [_terrain_thread, _grass_thread]:
		if thread != null and thread.is_started():
			thread.wait_to_finish()
	_terrain_thread = null
	_grass_thread = null


func height_at(x: float, z: float) -> float:
	return field.height(x, z)


## Runs the whole build. Awaits a frame between stages for the loading screen.
func build() -> void:
	await _stage("Surveying the clearing")
	if not is_inside_tree():
		return
	plan = ScenePlan.new(field)
	plan.build()
	# One height bake feeds the mesh, the collision and every scatterer.
	field.bake_height_grid(TerrainBuilder.COLLISION_HALF, TerrainBuilder.INNER_SPACING)

	# Background work that depends only on the immutable field and plan.
	terrain_material = _make_terrain_material()
	var terrain_builder := TerrainBuilder.new(field)
	_terrain_thread = Thread.new()
	_terrain_thread.start(func() -> Dictionary:
		return {
			mesh = terrain_builder.build_mesh(terrain_material),
			collision = terrain_builder.build_collision(),
			outer_collision = terrain_builder.build_outer_collision(),
		})
	var planter := GrassPlanter.new(field, Quality.current.grass_distance + GrassPlanter.CHUNK_SIZE, Quality.current.grass_density)
	_grass_thread = Thread.new()
	_grass_thread.start(planter.plan)

	await _stage("Growing the forest")
	if not is_inside_tree():
		return
	forest = Forest.new(field, plan)
	add_child(forest)
	forest.build()
	ridge_forest = RidgeForest.new(field, forest)
	add_child(ridge_forest)
	ridge_forest.build()

	await _stage("Shaping the land")
	var terrain_data: Variant = await _join(_terrain_thread)
	_terrain_thread = null
	if not is_inside_tree() or terrain_data == null:
		return
	_place_terrain(terrain_data.mesh, terrain_data.collision, terrain_data.outer_collision)

	await _stage("Planting the understory")
	if not is_inside_tree():
		return
	understory = Understory.new(field, plan)
	add_child(understory)
	understory.build_plants()
	await _join(_grass_thread)
	_grass_thread = null
	if not is_inside_tree():
		return
	understory.add_grass(planter)

	await _stage("Filling the pond")
	if not is_inside_tree():
		return
	pond = Pond.new(field)
	add_child(pond)
	pond.build()
	underwater_fx = UnderwaterFX.new(field)
	add_child(underwater_fx)
	shore_life = ShoreLife.new(field)
	add_child(shore_life)
	shore_life.build()

	await _stage("Setting up camp")
	if not is_inside_tree():
		return
	rocks = CampRocks.new(field, plan)
	add_child(rocks)
	rocks.build()
	campsite = Campsite.new(field)
	add_child(campsite)
	campsite.build()

	await _stage("Waking the wildlife")
	if not is_inside_tree():
		return
	wildlife = Wildlife.new(field)
	add_child(wildlife)
	wildlife.build()
	_setup_audio()
	await _stage("")


## Planar reflections are switched on last, once the renderer is warm.
func activate_reflections() -> void:
	if pond != null:
		pond.activate()


## Announces a stage and logs how long the previous one took: its CPU work and
## the first frame rendered after it (shader compilation, uploads).
func _stage(stage_name: String) -> void:
	var now := Time.get_ticks_msec()
	var previous := _stage_name
	var work_ms := now - _stage_started_ms
	if not stage_name.is_empty():
		stage_started.emit(stage_name, _stage_index, STAGE_COUNT)
	_stage_index += 1
	_stage_name = stage_name
	await get_tree().process_frame
	_stage_started_ms = Time.get_ticks_msec()
	if not previous.is_empty():
		print("  %-26s %5d ms work  %5d ms first frame" % [previous, work_ms, _stage_started_ms - now])


## Waits for a worker thread without blocking the render loop. Returns null if
## the scene went away in the meantime (the thread is joined by _exit_tree).
func _join(thread: Thread) -> Variant:
	while thread.is_alive():
		await get_tree().process_frame
		if not is_inside_tree():
			return null
	if not thread.is_started():
		return null
	return thread.wait_to_finish()


# ------------------------------------------------------------------- terrain

func _make_terrain_material() -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/terrain.gdshader")
	for set_name in ["grass", "litter", "mud", "path"]:
		bind_pbr_set(mat, set_name, set_name)
	bind_texture(mat, "noise_tex", "res://textures/noise_rgba.png")
	bind_texture(mat, "detail_normal", "res://textures/detail_normal.png")
	bind_texture(mat, "caustics_tex", "res://textures/caustics.png")
	return mat


func _place_terrain(mesh: ArrayMesh, collision: CollisionShape3D, outer_collision: CollisionShape3D) -> void:
	terrain = MeshInstance3D.new()
	terrain.name = "Terrain"
	terrain.mesh = mesh
	terrain.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	terrain.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	add_child(terrain)

	terrain_body = StaticBody3D.new()
	terrain_body.name = "TerrainBody"
	terrain_body.collision_layer = 1
	terrain_body.add_child(collision)
	terrain_body.add_child(outer_collision)
	add_child(terrain_body)
	Quality.preset_changed.connect(_on_quality)
	_on_quality(Quality.current)


func _on_quality(p: QualityPreset) -> void:
	if terrain_material != null:
		terrain_material.set_shader_parameter("parallax_steps", p.parallax_steps)


# ------------------------------------------------------------------- helpers

func _process(_delta: float) -> void:
	if audio == null or not audio.is_ready():
		return
	var world := Game.world as WorldController
	if world != null:
		audio.set_daylight(world.daylight())
		audio.set_wind(clampf((world.wind_strength() - 0.4) / 0.6, 0.0, 1.0))
	if campsite != null and campsite.firepit != null:
		audio.set_fire_intensity(campsite.firepit.intensity)


func _setup_audio() -> void:
	audio = AudioDirector.new()
	audio.name = "Audio"
	add_child(audio)
	Game.audio = audio
	var shore: Array[Vector3] = []
	for i in 8:
		var a := float(i) / 8.0 * TAU
		var p := TerrainField.shore_point(a)
		shore.append(Vector3(p.x, TerrainField.WATER_LEVEL + 0.05, p.y))
	var canopy: Array[Vector3] = []
	if plan != null:
		for entry in plan.near_trees():
			if canopy.size() >= 8:
				break
			if entry.position.length() < 18.0 or entry.position.length() > 55.0:
				continue
			var y := field.height(entry.position.x, entry.position.y) + 6.0
			canopy.append(Vector3(entry.position.x, y, entry.position.y))
	if canopy.is_empty():
		canopy.append(Vector3(20.0, 8.0, -18.0))
		canopy.append(Vector3(-16.0, 8.0, -22.0))
		canopy.append(Vector3(18.0, 7.0, 20.0))
	var fire_pos := Vector3(TerrainField.FIRE.x, 0.55, TerrainField.FIRE.y)
	if campsite != null:
		fire_pos = campsite.firepit.global_position + Vector3(0.0, 0.45, 0.0)
	audio.setup(fire_pos, shore, canopy)
	var player := Game.player as Player
	if player != null:
		if not player.footstep.is_connected(_on_footstep):
			player.footstep.connect(_on_footstep)
		if not player.wading_changed.is_connected(_on_wading):
			player.wading_changed.connect(_on_wading)


func _on_footstep(surface: StringName, running: bool) -> void:
	if audio != null:
		audio.footstep(surface, running)


func _on_wading(active: bool, speed: float) -> void:
	if audio != null:
		audio.set_wading(active, clampf(speed / 4.0, 0.0, 1.0))
	if pond != null:
		pond.set_wading(0.7 if active else 0.0)


static func bind_pbr_set(material: ShaderMaterial, prefix: String, set_name: String) -> void:
	bind_texture(material, prefix + "_albedo", "res://textures/%s_albedo.png" % set_name)
	bind_texture(material, prefix + "_normal", "res://textures/%s_normal.png" % set_name)
	bind_texture(material, prefix + "_orm", "res://textures/%s_orm.png" % set_name)


static func bind_prop_pbr(material: ShaderMaterial, set_name: String) -> void:
	bind_texture(material, "albedo_tex", "res://textures/%s_albedo.png" % set_name)
	bind_texture(material, "normal_tex", "res://textures/%s_normal.png" % set_name)
	bind_texture(material, "orm_tex", "res://textures/%s_orm.png" % set_name)


static func bind_texture(material: ShaderMaterial, uniform: String, path: String) -> void:
	if not ResourceLoader.exists(path):
		push_warning("Missing texture %s for %s" % [path, uniform])
		return
	material.set_shader_parameter(uniform, load(path))
