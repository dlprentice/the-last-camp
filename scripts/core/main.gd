extends Node3D

## Scene entry point: runs the staged world build behind the loading screen,
## warms the renderer up, then hands over to the intro dolly, the player, or
## the capture/benchmark tool depending on the command line.

@onready var camp: Camp = $Camp
@onready var world: WorldController = $World
@onready var player: Player = $Player
@onready var intro: IntroDolly = $Intro
@onready var loading: LoadingScreen = $Loading
@onready var hud: Hud = $HUD


const TOOL_LOADING_LIMIT_SECONDS := 300.0


func _ready() -> void:
	if Game.has_flag("package-check"):
		var result: int = preload("res://scripts/core/package_check.gd").run(Game.arg_value("notices", ""))
		Game.quit_cleanly(result)
		return
	Game.mode = Game.Mode.LOADING
	camp.stage_started.connect(_on_stage_started)
	if Game.is_tool_run():
		_watch_loading()
	var t0 := Time.get_ticks_msec()
	# The opaque loading UI needs only 2D. Avoid rendering half-built forest
	# stages while their CPU-side upload buffers are still resident.
	var restore_3d := get_viewport().disable_3d
	get_viewport().disable_3d = true
	await camp.build()
	if not is_inside_tree():
		return
	# Opt-in performance experiment: hero trees beyond 45 m become baked
	# billboards. Off by default because the demo draws the whole scene at
	# full distance; enable with -- --impostors to measure.
	if Game.has_flag("impostors"):
		var impostors := TreeImpostors.new()
		camp.add_child(impostors)
		loading.set_progress("Baking tree impostors", 0.90)
		await impostors.bake(camp.forest)
		if not is_inside_tree():
			return
		camp.forest.attach_impostors(impostors)
	$BiomeDressing.setup(camp)
	await get_tree().process_frame
	$ForestFloorDressing.setup(camp)
	var scanned := preload("res://scripts/camp/scanned_dressing.gd").new()
	camp.add_child(scanned)
	scanned.setup(camp)
	await get_tree().process_frame
	var habitat := preload("res://scripts/camp/habitat_diversity.gd").new()
	camp.add_child(habitat)
	habitat.setup(camp)
	var drips := preload("res://scripts/fx/canopy_drips.gd").new()
	camp.add_child(drips)
	drips.setup(camp, world)
	var director := $TerrainFieldDirector
	while director._rain_thread != null:
		await get_tree().process_frame
		if not is_inside_tree():
			return
	var t_built := Time.get_ticks_msec()
	loading.set_progress("Warming up the renderer", 0.96)
	get_viewport().disable_3d = restore_3d
	await world.warm_up()
	if not is_inside_tree():
		return
	camp.activate_reflections()
	await get_tree().process_frame
	var t_ready := Time.get_ticks_msec()
	print("World ready in %d ms (%d build + %d warm-up)" % [t_ready - t0, t_built - t0, t_ready - t_built])
	if Game.has_flag("scene-smoke"):
		var ok := camp.plan.trees.size() >= 1200 and habitat.batches.size() > 0 and camp.wildlife.fish_routes.size() >= 12
		print("SHOWCASE_SCENE_SMOKE trees=%d habitat_batches=%d fish=%d result=%s" % [camp.plan.trees.size(), habitat.batches.size(), camp.wildlife.fish_routes.size(), "PASS" if ok else "FAIL"])
		Game.quit_cleanly(0 if ok else 1)
		return
	if Game.has_flag("check-pond"):
		loading.finish()
		var capture := CaptureTool.new()
		add_child(capture)
		capture.run_pond_check(Game.arg_value("out", "res://local-data/pond-check"))
		return
	if Game.has_flag("review-assets"):
		loading.finish()
		var review := AssetReview.new()
		add_child(review)
		review.run(Game.arg_value("review-assets", "res://local-data/asset-review"))
		return
	if Game.has_flag("benchmark"):
		loading.finish()
		var capture := CaptureTool.new()
		add_child(capture)
		capture.run_benchmark(Game.arg_value("benchmark", ""), Game.arg_value("out", "user://benchmark.json"))
		return
	if Game.has_flag("cinematic"):
		loading.finish()
		var cinematic := Cinematic.new()
		add_child(cinematic)
		cinematic.play(Game.arg_value("cinematic", "showreel"))
		return
	if Game.has_flag("profile"):
		loading.finish()
		var capture := CaptureTool.new()
		add_child(capture)
		capture.run_profile(Game.arg_value("out", "user://profile.json"))
		return
	if Game.has_flag("capture"):
		loading.finish()
		var capture := CaptureTool.new()
		add_child(capture)
		capture.run_capture(Game.arg_value("capture", "res://local-data/captures"), Game.arg_value("shots", ""))
		return
	loading.finish()
	if Game.has_flag("skip-intro"):
		player.begin()
		if Game.has_flag("traverse"):
			var traversal := TraversalCheck.new()
			add_child(traversal)
			traversal.run(player, Game.arg_value("traverse", "res://local-data/traversal"))
	else:
		intro.play(player)
	Quality.auto_tune()


func _on_stage_started(stage_name: String, index: int, total: int) -> void:
	loading.set_progress(stage_name, float(index) / float(total + 1))


## Automated runs (captures, benchmarks, profiles, traversals, cinematics)
## must never sit on the loading screen forever: a script error during the
## build used to leave a fullscreen window up until someone killed it.
func _watch_loading() -> void:
	await get_tree().create_timer(TOOL_LOADING_LIMIT_SECONDS).timeout
	if is_inside_tree() and Game.mode == Game.Mode.LOADING:
		push_error("World build did not finish within %d s; quitting the tool run" % int(TOOL_LOADING_LIMIT_SECONDS))
		print("LOADING_TIMEOUT seconds=%d" % int(TOOL_LOADING_LIMIT_SECONDS))
		get_tree().quit(1)
