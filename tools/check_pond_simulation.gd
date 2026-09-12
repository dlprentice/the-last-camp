extends SceneTree
## A small real-GPU check, independent of the camp scene and its rendering cost.
## godot --path . --fullscreen --script res://tools/check_pond_simulation.gd
## State readbacks occur only at checkpoints, never in the runtime simulation.

const Simulation = preload("res://scripts/world/pond_simulation.gd")
const SIZE := 64
const BOUNDS := Rect2(0, 0, 8, 8)

var _failures := 0
var _report := {}
var _out := ""


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_out = ProjectSettings.globalize_path("res://local-data/water-interaction-20260907/solver-%d-%d" % [Time.get_unix_time_from_system(), Time.get_ticks_msec()])
	DirAccess.make_dir_recursive_absolute(_out)
	if DisplayServer.get_name() == "headless":
		push_error("This focused check requires the real main RenderingDevice.")
		quit(2)
		return
	DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)
	var mask := Image.create(SIZE, SIZE, false, Image.FORMAT_RF)
	mask.fill(Color(0, 0, 0, 0))
	for z in SIZE:
		mask.set_pixel(32, z, Color(1, 0, 0))
	var blocked := Simulation.new()
	var open_water := Simulation.new()
	_check(blocked.setup(BOUNDS, mask, SIZE), "main GPU setup queued")
	_check(open_water.setup(BOUNDS, null, SIZE), "open reference setup queued")
	for frame in 180:
		if blocked.is_ready() and open_water.is_ready():
			break
		await process_frame
	if not blocked.is_ready() or not open_water.is_ready():
		_check(false, "GPU setup completed")
		blocked.shutdown()
		open_water.shutdown()
		_finish()
		return
	_check(true, "GPU setup completed")
	var stable_rid := (blocked.get_texture() as Texture2DRD).texture_rd_rid
	var initial := blocked.get_texture().get_image()
	_check(_stats(initial).height_max < 0.0000001, "initial state is neutral")
	blocked.queue_impulse(Vector2(2.5, 4.0), 0.24, 0.45)
	open_water.queue_impulse(Vector2(2.5, 4.0), 0.24, 0.45)
	# A half-timestep call must retain the event; its second half dispatches it.
	blocked.step(Simulation.FIXED_STEP * 0.5)
	_check(blocked.get_simulation_time() == 0.0, "fractional step retained")
	blocked.step(Simulation.FIXED_STEP * 0.5)
	open_water.step(Simulation.FIXED_STEP)
	await process_frame
	await RenderingServer.frame_post_draw
	var first := blocked.get_texture().get_image()
	var first_stats := _stats(first)
	_report["first_impulse"] = first_stats
	_check(first_stats.velocity_max > 0.1, "queued impulse is not lost below the fixed timestep")
	await _advance([blocked, open_water], 15)
	var early := blocked.get_texture().get_image()
	var early_stats := _stats(early)
	_report["early_1_second"] = early_stats
	_check(early_stats.height_max > 0.0001, "persistent nonzero displacement")
	_check(_region_max(early, Rect2i(4, 24, 7, 16)) > 0.00001, "impulse propagates beyond its input footprint")
	_check(early_stats.foam_max > 0.001, "contacts create persistent foam")
	_save_state(early, "early")
	await _advance([blocked, open_water], 30)
	var reflected := blocked.get_texture().get_image()
	var reference := open_water.get_texture().get_image()
	_report["reflected_3_seconds"] = _stats(reflected)
	var blocked_right := _region_max(reflected, Rect2i(33, 0, 31, SIZE))
	var open_right := _region_max(reference, Rect2i(33, 0, 31, SIZE))
	var return_difference := _region_difference(reflected, reference, Rect2i(10, 20, 14, 24))
	_report["boundary"] = {"blocked_right_height": blocked_right, "open_right_height": open_right, "returned_difference": return_difference}
	_check(blocked_right < 0.0000001, "solid strip prevents wave leakage")
	_check(open_right > 0.00001, "open reference wave crosses the same location")
	_check(return_difference > 0.00001, "solid boundary returns a wave into the first basin")
	_save_state(reflected, "reflected")
	# Freeze after one gather; its exact source field remains available while
	# the small asynchronous readback arrives over the normal frame queue.
	var points := PackedVector2Array([Vector2(2.231, 3.877), Vector2(1.518, 4.411), Vector2(6.17, 2.7), Vector2(-1, 2)])
	blocked.set_probe_positions(points, 10)
	blocked.step(1.0 / 30.0)
	await process_frame
	await RenderingServer.frame_post_draw
	var gathered_image := blocked.get_texture().get_image()
	var age_at_dispatch := blocked.get_probe_sample_time()
	for frame in 30:
		if blocked.get_probe_sample_time() >= 0.0:
			break
		await process_frame
	var sampled: PackedFloat32Array = blocked.get_probe_heights()
	var maximum_error := 0.0
	for i in points.size():
		maximum_error = maxf(maximum_error, absf(sampled[i] - _bilinear_height(gathered_image, points[i])))
	_report["probes"] = {"maximum_error": maximum_error, "sample_time": blocked.get_probe_sample_time(), "time_at_dispatch": age_at_dispatch}
	_check(blocked.get_probe_sample_time() >= 0.0, "asynchronous probes complete")
	_check(blocked.get_sampled_probe_positions() == points, "probe positions are returned with their samples")
	_check(maximum_error < 0.000001, "GPU probes match exact bilinear surface samples")
	blocked.set_probe_positions(points, 11)
	blocked.step(1.0 / 30.0)
	blocked.set_probe_positions(PackedVector2Array([Vector2(6.17, 2.7)]), 12)
	for frame in 8:
		await process_frame
	_check(blocked.get_probe_sample_time() < 0.0 and blocked.get_probe_heights().size() == 1 and blocked.get_probe_heights()[0] == 0.0, "old probe layout cannot populate new body slots")
	blocked.set_probe_positions(PackedVector2Array(), 13)
	await _advance([blocked, open_water], 210)
	var settled := blocked.get_texture().get_image()
	var settled_stats := _stats(settled)
	_report["settled_17_seconds"] = settled_stats
	_check(settled_stats.finite, "all state channels remain finite")
	_check(settled_stats.height_max <= Simulation.MAX_HEIGHT + 0.000001 and settled_stats.velocity_max <= Simulation.MAX_VELOCITY + 0.000001, "state respects safety limits")
	_check(settled_stats.energy < early_stats.energy * 0.05, "wave energy decays without new input")
	_check(settled_stats.foam_sum < early_stats.foam_sum * 0.15, "persistent foam dissipates after motion settles")
	_check((blocked.get_texture() as Texture2DRD).texture_rd_rid == stable_rid, "published texture RID stays stable")
	_save_state(settled, "settled")
	blocked.shutdown()
	open_water.shutdown()
	for frame in 4:
		await process_frame
	_check(not blocked.is_ready() and blocked.get_texture().get_image().get_pixel(0, 0).r == 0.0, "shutdown returns neutral fallback")
	_finish()


func _advance(simulations: Array, frames: int) -> void:
	for frame in frames:
		for simulation in simulations:
			simulation.step(1.0 / 15.0, Vector2(0.08, 0.025))
		await process_frame
	await RenderingServer.frame_post_draw


func _stats(image: Image) -> Dictionary:
	var finite := true
	var height_max := 0.0
	var velocity_max := 0.0
	var foam_max := 0.0
	var foam_sum := 0.0
	var energy := 0.0
	var spacing := BOUNDS.size.x / float(SIZE)
	for z in SIZE:
		for x in SIZE:
			var value := image.get_pixel(x, z)
			finite = finite and is_finite(value.r) and is_finite(value.g) and is_finite(value.b) and is_finite(value.a)
			height_max = maxf(height_max, absf(value.r))
			velocity_max = maxf(velocity_max, absf(value.g))
			foam_max = maxf(foam_max, value.b)
			foam_sum += value.b
			if value.a < 0.5:
				continue
			energy += value.g * value.g
			for offset in [Vector2i(1, 0), Vector2i(0, 1)]:
				var neighbour: Vector2i = Vector2i(x, z) + offset
				if neighbour.x >= SIZE or neighbour.y >= SIZE:
					continue
				var other := image.get_pixelv(neighbour)
				if other.a > 0.5:
					energy += pow(Simulation.WAVE_SPEED * (other.r - value.r) / spacing, 2)
	return {"finite": finite, "height_max": height_max, "velocity_max": velocity_max, "foam_max": foam_max, "foam_sum": foam_sum, "energy": energy * spacing * spacing}


func _region_max(image: Image, rect: Rect2i) -> float:
	var result := 0.0
	for z in range(rect.position.y, rect.end.y):
		for x in range(rect.position.x, rect.end.x):
			result = maxf(result, absf(image.get_pixel(x, z).r))
	return result


func _region_difference(a: Image, b: Image, rect: Rect2i) -> float:
	var result := 0.0
	for z in range(rect.position.y, rect.end.y):
		for x in range(rect.position.x, rect.end.x):
			result = maxf(result, absf(a.get_pixel(x, z).r - b.get_pixel(x, z).r))
	return result


func _bilinear_height(image: Image, point: Vector2) -> float:
	var uv := (point - BOUNDS.position) / BOUNDS.size
	if uv.x < 0.0 or uv.y < 0.0 or uv.x >= 1.0 or uv.y >= 1.0:
		return 0.0
	var pixel := uv * float(SIZE) - Vector2(0.5, 0.5)
	var base := Vector2i(floori(pixel.x), floori(pixel.y))
	var f := pixel - Vector2(base)
	var result := 0.0
	for z in 2:
		for x in 2:
			var sample_cell := (base + Vector2i(x, z)).clamp(Vector2i.ZERO, Vector2i(SIZE - 1, SIZE - 1))
			var weight := (1.0 - f.x if x == 0 else f.x) * (1.0 - f.y if z == 0 else f.y)
			result += image.get_pixelv(sample_cell).r * weight
	return result


func _save_state(image: Image, label: String) -> void:
	var view := Image.create(SIZE, SIZE, false, Image.FORMAT_RGB8)
	for z in SIZE:
		for x in SIZE:
			var value := image.get_pixel(x, z)
			var colour := Color(0.06, 0.06, 0.06) if value.a < 0.5 else Color(0.1 + maxf(value.r, 0.0) * 15.0, 0.15 + value.b * 2.0, 0.3 + maxf(-value.r, 0.0) * 15.0)
			view.set_pixel(x, z, colour)
	view.resize(512, 512, Image.INTERPOLATE_NEAREST)
	view.save_png(_out.path_join(label + ".png"))


func _check(condition: bool, label: String) -> void:
	print("POND_CHECK %s %s" % ["PASS" if condition else "FAIL", label])
	if not condition:
		_failures += 1


func _finish() -> void:
	_report["failures"] = _failures
	var file := FileAccess.open(_out.path_join("report.json"), FileAccess.WRITE)
	file.store_string(JSON.stringify(_report, "\t"))
	print("POND_CHECK_DONE failures=%d output=%s" % [_failures, _out])
	quit(1 if _failures > 0 else 0)
