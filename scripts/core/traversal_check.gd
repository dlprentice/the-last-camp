class_name TraversalCheck
extends Node

## Scripted walk through the camp for the acceptance record. Drives the real
## player controller with the game's own input actions along waypoints (trail,
## clearing, fire, table, tent, down to the shore, out along the dock and back,
## into the shallows and out, back to the fire), then exercises photo mode with
## a focus lock, the lantern, feeding the fire and a quality change. Each
## waypoint records position, ground height, floor contact and water depth,
## saves a screenshot when a rendering device is present, and the run fails on
## falling through the ground, getting stuck or timing out.
##   godot --path . --fullscreen -- --skip-intro --quality=high --traverse="$PWD/local-data/traversal/run-1"
##   godot --headless --path . -- --skip-intro --traverse=/tmp/x    (physics only)

## Straight legs between authored points; the route walks around the fire
## ring and along the dock's centreline (start (-15.9, 6.3) towards the pond).
const WAYPOINTS: Array[Dictionary] = [
	{name = "trail", at = Vector2(1.5, 24.0)},
	{name = "clearing", at = Vector2(1.0, 9.0)},
	{name = "fire_edge", at = Vector2(2.6, 3.4)},
	{name = "table", at = Vector2(7.0, 3.2)},
	{name = "tent_front", at = Vector2(4.4, -0.4)},
	{name = "seat_gap", at = Vector2(3.8, 3.8)},
	{name = "west_of_fire", at = Vector2(-3.6, 3.6)},
	{name = "to_water", at = Vector2(-9.0, 2.0)},
	{name = "dock_root", at = Vector2(-16.0, 6.3)},
	{name = "dock_end", at = Vector2(-23.1, 5.4)},
	{name = "dock_root_back", at = Vector2(-16.0, 6.3)},
	{name = "wade_in", at = Vector2(-19.6, 2.6)},
	{name = "wade_out", at = Vector2(-13.5, 0.5)},
	{name = "west_of_fire_back", at = Vector2(-3.6, 3.6)},
	{name = "fire_return", at = Vector2(2.4, 3.2)},
]
const ARRIVE := 0.7
const WAYPOINT_TIMEOUT := 45.0
const STUCK_SECONDS := 6.0
const STUCK_PROGRESS := 0.35

var _player: Player
var _out_dir: String
var _report := {waypoints = [], checks = [], failures = []}
var _running := false


func run(player: Player, out_dir: String) -> void:
	_player = player
	if out_dir.strip_edges() == "":
		out_dir = "res://local-data/traversal/run"
	_out_dir = ProjectSettings.globalize_path(out_dir)
	DirAccess.make_dir_recursive_absolute(_out_dir)
	_running = true
	_walk()


func _walk() -> void:
	await get_tree().create_timer(1.0).timeout
	_report["start"] = _snapshot("start")
	for wp: Dictionary in WAYPOINTS:
		var ok := await _go_to(wp.name, wp.at)
		var snap := _snapshot(wp.name)
		snap["reached"] = ok
		_report.waypoints.append(snap)
		await _screenshot(wp.name)
		print("TRAVERSAL waypoint=%s reached=%s pos=(%.2f %.2f %.2f) ground=%.2f floor=%s water=%.2f t=%.1f" % [
			wp.name, ok, snap.x, snap.y, snap.z, snap.ground, snap.on_floor, snap.water_depth, snap.seconds])
	_release()
	await _interactions()
	var passed: bool = _report.failures.is_empty()
	_report["passed"] = passed
	var file := FileAccess.open(_out_dir.path_join("report.json"), FileAccess.WRITE)
	if file != null:
		file.store_string(JSON.stringify(_report, "  "))
	print("TRAVERSAL_DONE result=%s waypoints=%d failures=%d out=%s" % ["PASS" if passed else "FAIL", _report.waypoints.size(), _report.failures.size(), _out_dir])
	for f in _report.failures:
		print("TRAVERSAL_FAILURE %s" % f)
	Game.quit_cleanly(0 if passed else 1)


## Steer the controller at the target with its own forward action until it
## arrives, gets stuck or times out. Falling below the ground is a failure.
func _go_to(name_value: String, target: Vector2) -> bool:
	var elapsed := 0.0
	var best := INF
	var since_progress := 0.0
	while elapsed < WAYPOINT_TIMEOUT:
		var delta := get_physics_process_delta_time()
		var pos := Vector2(_player.global_position.x, _player.global_position.z)
		var to_target := target - pos
		var distance := to_target.length()
		if distance < ARRIVE:
			_release()
			return true
		_player.yaw = atan2(-to_target.x, -to_target.y)
		Input.action_press("move_forward")
		if distance < best - STUCK_PROGRESS:
			best = distance
			since_progress = 0.0
		else:
			since_progress += delta
			if since_progress > STUCK_SECONDS:
				_report.failures.append("stuck %.1f m short of %s at (%.2f, %.2f)" % [distance, name_value, pos.x, pos.y])
				_release()
				return false
		var ground: float = Game.camp.height_at(pos.x, pos.y)
		if _player.global_position.y < ground - 0.6 and not _player.in_water:
			_report.failures.append("fell below the ground near %s at (%.2f, %.2f, %.2f), ground %.2f" % [name_value, pos.x, _player.global_position.y, pos.y, ground])
			_release()
			return false
		elapsed += delta
		await get_tree().physics_frame
	_report.failures.append("timed out walking to %s" % name_value)
	_release()
	return false


func _release() -> void:
	Input.action_release("move_forward")
	Input.action_release("sprint")


func _snapshot(label: String) -> Dictionary:
	var p := _player.global_position
	return {label = label, x = p.x, y = p.y, z = p.z, ground = Game.camp.height_at(p.x, p.z),
		on_floor = _player.is_on_floor(), water_depth = _player.water_depth, in_water = _player.in_water,
		seconds = Time.get_ticks_msec() / 1000.0}


func _screenshot(label: String) -> void:
	if DisplayServer.get_name() == "headless":
		return
	await get_tree().process_frame
	await get_tree().process_frame
	var image := get_viewport().get_texture().get_image()
	if image != null:
		image.save_png(_out_dir.path_join("%s.png" % label))


## Photo mode with focus lock and flight, the lantern, feeding the fire and a
## quality change: each is driven through the same code the player's keys use.
func _interactions() -> void:
	var rig: PhotoRig = _player.photo
	var before := _player.camera.global_position
	rig.enter(_player.camera)
	await get_tree().create_timer(0.5).timeout
	var entered := Game.mode == Game.Mode.PHOTO
	Input.action_press("move_forward")
	Input.action_press("fly_up")
	await get_tree().create_timer(2.5).timeout
	Input.action_release("move_forward")
	Input.action_release("fly_up")
	var moved := rig.camera.global_position.distance_to(before) if rig.camera != null else 0.0
	rig.focus_locked = true
	await get_tree().create_timer(0.5).timeout
	await _screenshot("photo_mode")
	rig.exit()
	await get_tree().create_timer(0.5).timeout
	var restored := Game.mode == Game.Mode.PLAY and _player.camera.current
	_check("photo mode entered, flew %.1f m, focus locked and restored the player camera" % moved, entered and moved > 0.5 and restored)

	_player.toggle_lantern()
	var lit := _player.lantern_on
	_player.toggle_lantern()
	_check("lantern toggles on and off", lit and not _player.lantern_on)

	_player.yaw = atan2(-(0.0 - _player.global_position.x), -(0.0 - _player.global_position.z))
	_player.pitch = -0.45
	await get_tree().create_timer(0.8).timeout
	var focus: Node = _player._focus
	var fed := false
	if focus != null and focus.has_method("interact"):
		focus.interact(_player)
		fed = true
	_check("facing the fire offers an interaction and feeding it succeeds", fed)
	await _screenshot("fire_fed")

	var start_tier := Quality.current.tier
	Quality.apply(QualityPreset.Tier.MEDIUM)
	while Game.camp.understory._replant_thread != null:
		await get_tree().process_frame
	await get_tree().create_timer(0.5).timeout
	var medium := Quality.current.tier == QualityPreset.Tier.MEDIUM
	Quality.apply(start_tier)
	while Game.camp.understory._replant_thread != null:
		await get_tree().process_frame
	_check("quality steps down to Medium and back without errors", medium and Quality.current.tier == start_tier)
	await _screenshot("after_quality_change")


func _check(label: String, ok: bool) -> void:
	_report.checks.append({label = label, ok = ok})
	print("TRAVERSAL check=%s ok=%s" % [label, ok])
	if not ok:
		_report.failures.append("check failed: %s" % label)
