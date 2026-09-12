class_name CaptureTool
extends Node

## Headless-friendly verification tool.
##
## `--capture=DIR` renders a fixed set of viewpoints to PNG files so visual
## changes can be reviewed without playing; `--benchmark[=low,medium,...]`
## flies a camera path per quality preset and writes frame statistics as JSON.

## SDFGI and the temporal volumetrics take a few seconds to converge after a
## teleport; a still taken earlier shows distant canopies too pale.
const SETTLE_SECONDS := 7.0
const BENCH_SECONDS := 22.0
const WARMUP_SECONDS := 3.0

## Named viewpoints: position, look target and an optional hour override.
const VIEWPOINTS: Array[Dictionary] = [
	{name = "linen_dry", pos = Vector3(7.8, 1.55, 3.3), look = Vector3(9.2, 0.87, 1.2), fov = 48.0, hour = 17.5},
	{name = "linen_wet", pos = Vector3(7.8, 1.55, 3.3), look = Vector3(9.2, 0.87, 1.2), fov = 48.0, hour = 17.5, weather = "dawn"},
	{name = "bark_dry", pos = Vector3(14.0, 1.7, -10.0), look = Vector3(18.0, 5.0, -22.0), fov = 42.0, hour = 17.5},
	{name = "bark_wet", pos = Vector3(14.0, 1.7, -10.0), look = Vector3(18.0, 5.0, -22.0), fov = 42.0, hour = 17.5, weather = "dawn"},
	{name = "rain_deck", pos = Vector3(-20.65, -0.02, 6.07), look = Vector3(-23.5, -0.28, 5.20), absolute = true, fov = 48.0, hour = 21.92, weather = "rain_detail"},
	{name = "dry_deck", pos = Vector3(-20.65, -0.02, 6.07), look = Vector3(-23.5, -0.28, 5.20), absolute = true, fov = 48.0, hour = 21.92},
	{name = "rain_canvas", pos = Vector3(6.9, 1.25, 0.0), look = Vector3(7.5, 0.8, -4.5), fov = 48.0, hour = 21.55, weather = "storm", weather_time = 33.08, weather_duration = 40.0},
	{name = "rain_rocks", pos = Vector3(-14.0, 0.70, 8.4), look = Vector3(-19.0, 0.0, 6.0), fov = 48.0, hour = 21.55, weather = "storm", weather_time = 33.02, weather_duration = 40.0},
	{name = "fire_detail", pos = Vector3(0.1, 0.85, 2.5), look = Vector3(0, 0.67, 0), fov = 45.0, hour = 20.2},
	{name = "fire_side", pos = Vector3(-2.2, 0.75, 0.3), look = Vector3(0, 0.60, 0), fov = 44.0, hour = 20.2},
	{name = "fire_fed", pos = Vector3(0.1, 0.85, 2.5), look = Vector3(0, 0.67, 0), fov = 45.0, hour = 20.2, fire = 2.0},
	{name = "lilies", pos = Vector3(-27.5, -0.19, 11.5), look = Vector3(-29.2, -0.87, 14.4), absolute = true, fov = 40.0, hour = 19.15},
	{name = "table_join", pos = Vector3(7.8, 0.65, 2.7), look = Vector3(9.2, 0.74, 1.2), fov = 43.0},
	{name = "tent_side", pos = Vector3(6.9, 1.25, 0.0), look = Vector3(7.5, 0.8, -4.5), fov = 48.0},
	{name = "trail", pos = Vector3(2.7, 1.55, 15.0), look = Vector3(1.0, 0.08, 5.0), fov = 64.0},
	{name = "kitchen", pos = Vector3(7.8, 1.55, 3.3), look = Vector3(9.2, 0.87, 1.2), fov = 48.0},
	{name = "flowers", pos = Vector3(6.8, 0.72, 9.4), look = Vector3(5.5, 0.45, 8.0), fov = 44.0},
	{name = "arrival", pos = Vector3(3.2, 1.72, 15.0), look = Vector3(-14.0, 0.9, 3.5)},
	{name = "fire", pos = Vector3(4.6, 1.55, 3.8), look = Vector3(0.0, 0.7, 0.0)},
	{name = "pond", pos = Vector3(-15.5, 1.7, 9.0), look = Vector3(-42.0, 0.5, -2.0)},
	{name = "dock", pos = Vector3(-21.0, 1.5, 6.3), look = Vector3(-45.0, 3.0, 12.0)},
	{name = "treeline", pos = Vector3(-6.0, 1.7, -6.0), look = Vector3(12.0, 7.0, -40.0)},
	{name = "tent", pos = Vector3(2.2, 1.6, -0.6), look = Vector3(7.5, 0.9, -4.5)},
	{name = "night_tent", pos = Vector3(2.2, 1.6, -0.6), look = Vector3(7.5, 0.9, -4.5), hour = 23.4},
	{name = "camp", pos = Vector3(6.2, 1.5, 3.6), look = Vector3(3.0, 0.4, -2.6)},
	{name = "smoke", pos = Vector3(3.4, 1.2, 3.2), look = Vector3(0.0, 2.6, 0.0)},
	{name = "lantern", pos = Vector3(2.2, 1.45, 11.9), look = Vector3(3.6, 1.35, 13.1)},
	{name = "lantern_side", pos = Vector3(2.6, 1.4, 14.6), look = Vector3(3.6, 1.35, 13.1)},
	{name = "lantern_back", pos = Vector3(5.2, 1.3, 14.2), look = Vector3(3.6, 1.35, 13.1)},
	{name = "tent_post", pos = Vector3(4.2, 1.4, -0.4), look = Vector3(5.9, 1.2, -3.1)},
	{name = "ground", pos = Vector3(1.5, 0.7, 12.0), look = Vector3(-2.0, -0.2, 7.0)},
	{name = "canopy", pos = Vector3(14.0, 1.7, -10.0), look = Vector3(18.0, 13.0, -22.0)},
	# Elevated views of the whole woodland, for judging how filled-in the
	# distant forest reads and where the near trees hand over to the ridges.
	{name = "fire_smoke_day", pos = Vector3(2.6, 1.25, 2.3), look = Vector3(0.0, 1.15, 0.0), fov = 44.0, hour = 10.5},
	{name = "oak_base", pos = Vector3(20.5, 1.1, -9.2), look = Vector3(17.5, 0.6, -11.5), absolute = true, fov = 46.0, hour = 17.5, weather = "dawn"},
	{name = "aftermath2", pos = Vector3(20.8, 1.3, -10.3), look = Vector3(12.0, 1.9, -5.8), absolute = true, fov = 50.0, hour = 17.5, weather = "dawn"},
	{name = "aerial_west", pos = Vector3(12.0, 42.0, 34.0), look = Vector3(-140.0, 12.0, -120.0), absolute = true, fov = 60.0, hour = 9.5},
	{name = "aerial_ridge", pos = Vector3(-30.0, 70.0, 60.0), look = Vector3(-60.0, 30.0, -420.0), absolute = true, fov = 55.0, hour = 9.5},
	{name = "overview", pos = Vector3(48.0, 26.0, 54.0), look = Vector3(-8.0, 1.0, -2.0)},
	{name = "intro_start", pos = Vector3(16.3, 8.0, 52.0), look = Vector3(-4.0, 3.5, 4.0), absolute = true},
	{name = "intro_quarter", pos = Vector3(6.4, 3.9, 40.0), look = Vector3(-5.0, 2.4, 3.0), absolute = true},
	{name = "intro_half", pos = Vector3(7.9, 4.2, 28.0), look = Vector3(-8.0, 1.4, 2.0), absolute = true},
	{name = "intro_mid", pos = Vector3(13.0, 4.6, 26.0), look = Vector3(-9.0, 1.2, 2.0), absolute = true},
	{name = "canoe", pos = Vector3(-19.5, 1.5, 0.4), look = Vector3(-25.0, -0.6, 4.8)},
	{name = "dock_end", pos = Vector3(-24.5, 1.6, 6.9), look = Vector3(-8.0, 0.6, 1.0)},
	{name = "underwater", pos = Vector3(-24.0, -1.6, 4.0), look = Vector3(-30.0, -1.9, 7.0), absolute = true},
	{name = "underwater_sun", pos = Vector3(-23.8, -2.25, 3.4), look = Vector3(-30.8, 1.5, 2.8), absolute = true, fov = 60.0, hour = 17.4},
	{name = "underwater_evening", pos = Vector3(-23.8, -2.25, 3.4), look = Vector3(-30.8, 1.5, 2.8), absolute = true, fov = 60.0, hour = 19.18},
	{name = "night_fire", pos = Vector3(4.6, 1.55, 3.8), look = Vector3(0.0, 0.7, 0.0), hour = 23.4},
	{name = "night_pond", pos = Vector3(-15.5, 1.7, 9.0), look = Vector3(-42.0, 0.5, -2.0), hour = 23.4},
	{name = "noon", pos = Vector3(32.0, 15.0, 36.0), look = Vector3(-4.0, 2.0, 0.0), hour = 13.0},
	{name = "aftermath", pos = Vector3(22.9, 1.5, -12.2), look = Vector3(14.0, 3.0, -9.5), hour = 17.5, weather = "dawn"},
	{name = "hero_oak", pos = Vector3(24.5, 1.6, -5.5), look = Vector3(17.5, 5.5, -11.5), fov = 55.0, hour = 17.0},
	{name = "hero_oak_fork", pos = Vector3(21.0, 3.0, -8.5), look = Vector3(17.5, 7.0, -11.5), fov = 50.0, hour = 17.0},
	{name = "specimen_pine", pos = Vector3(-7.0, 1.6, -13.0), look = Vector3(-13.0, 6.0, -19.0), fov = 55.0, hour = 17.0},
	{name = "specimen_alder", pos = Vector3(15.0, 1.6, 8.0), look = Vector3(21.0, 5.5, 13.5), fov = 55.0, hour = 17.0},
	{name = "dawn", pos = Vector3(-15.5, 1.7, 9.0), look = Vector3(-42.0, 0.5, -2.0), hour = 6.1},
	{name = "woodpile", pos = Vector3(3.8, 0.85, -0.2), look = Vector3(4.6, 0.30, -2.2), fov = 42.0, hour = 17.5},
	{name = "woodpile_night", pos = Vector3(3.8, 0.85, -0.2), look = Vector3(4.6, 0.30, -2.2), fov = 42.0, hour = 22.5},
	{name = "path_grazing", pos = Vector3(-6.0, 0.9, 1.0), look = Vector3(-14.0, 0.3, 3.5)},
	{name = "grass_near", pos = Vector3(9.0, 1.1, 8.0), look = Vector3(-4.0, 0.3, 2.0)},
	{name = "waterline", pos = Vector3(-22.0, -0.88, 9.5), look = Vector3(-30.0, -0.7, 4.0), absolute = true},
	{name = "waterline_low", pos = Vector3(-22.0, -0.93, 9.5), look = Vector3(-30.0, -0.8, 4.0), absolute = true},
	{name = "breach", pos = Vector3(-29.0, 0.7, 1.2), look = Vector3(-16.0, 1.2, 6.0), absolute = true},
	{name = "night_dock", pos = Vector3(-14.0, 1.6, 9.0), look = Vector3(-24.5, 0.5, 5.0), hour = 23.4},
	{name = "night_meadow", pos = Vector3(7.0, 1.55, 7.0), look = Vector3(0.5, 0.9, -1.0), hour = 23.4},
	{name = "night_lantern", pos = Vector3(2.2, 1.45, 11.9), look = Vector3(3.6, 1.35, 13.1), hour = 23.4},
]

const BENCH_PATH: Array[Vector3] = [
	Vector3(6.0, 1.7, 22.0), Vector3(4.0, 1.7, 10.0), Vector3(-4.0, 1.7, 4.0),
	Vector3(-12.0, 1.7, 3.0), Vector3(-20.0, 1.6, 6.0), Vector3(-14.0, 1.7, 12.0),
	Vector3(-2.0, 1.7, 10.0), Vector3(8.0, 1.7, -2.0), Vector3(4.0, 1.7, -10.0),
]
const BENCH_LOOK: Array[Vector3] = [
	Vector3(-10.0, 1.0, 2.0), Vector3(-6.0, 1.0, 0.0), Vector3(-30.0, 1.0, 0.0),
	Vector3(-40.0, 2.0, 4.0), Vector3(-45.0, 3.0, 10.0), Vector3(0.0, 1.0, 0.0),
	Vector3(0.0, 1.0, 0.0), Vector3(2.0, 2.0, -20.0), Vector3(0.0, 0.8, 0.0),
]

var camera: Camera3D


func _ready() -> void:
	camera = Camera3D.new()
	camera.fov = 68.0
	camera.near = 0.05
	camera.far = 1600.0
	camera.cull_mask = 0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)
	add_child(camera)
	camera.make_current()
	if Game.world != null:
		camera.attributes = Game.world.attributes
	Game.mode = Game.Mode.PHOTO
	Game.hud_visible = false


func run_capture(dir: String, filter: String) -> void:
	DirAccess.make_dir_recursive_absolute(dir)
	var wanted := PackedStringArray()
	if filter != "":
		wanted = filter.split(",", false)
	var base_hour: float = Game.world.hour
	var viewpoints: Array[Dictionary] = VIEWPOINTS.duplicate()
	viewpoints.append_array(_prop_viewpoints())
	if Game.has_flag("capture-sequence"):
		if Game.player != null:
			Game.player.set_physics_process(false)
			Game.player.global_position = Vector3(10000, 10, 10000)
		viewpoints.clear()
		var shots := Cinematic.sequence(Game.arg_value("capture-sequence", "afterglow"))
		var samples: Array[float] = []
		for value in Game.arg_value("capture-samples", "0.5").split(",", false):
			if not value.is_valid_float() or float(value) < 0.0 or float(value) > 1.0:
				push_error("capture-samples expects fractions from 0 to 1")
				get_tree().quit(1)
				return
			samples.append(float(value))
		var fire_level := 1.0
		var lanterns_on := true
		var wind := 1.0
		for i in shots.size():
			var shot := shots[i]
			var path := Spline.new(Cinematic.resolve(Game.camp.field, shot.path, shot.absolute))
			var look := Spline.new(Cinematic.resolve(Game.camp.field, shot.look, shot.absolute))
			if not is_nan(shot.fire):
				fire_level = shot.fire
			if shot.lantern_state >= 0:
				lanterns_on = shot.lantern_state == 1
			for progress in samples:
				var u := Cinematic.motion_progress(shot, shot.duration * progress)
				var pose := Cinematic.camera_position(shot, path, Game.camp.field, shot.duration * progress)
				var hour := shot.hour if not is_nan(shot.hour) else base_hour
				if not is_nan(shot.hour_end):
					hour = lerpf(hour, shot.hour_end, progress)
				var lit := lanterns_on or (not is_nan(shot.lantern_hour) and hour >= shot.lantern_hour)
				var gust := wind
				if not is_nan(shot.wind):
					gust = lerpf(shot.wind, shot.wind_end if not is_nan(shot.wind_end) else shot.wind, progress)
				var base := "film_%02d" % (i + 1)
				var name := base
				if samples.size() > 1:
					name += "_p%03d" % roundi(progress * 100)
				viewpoints.append({name = name, base = base, pos = pose,
					look = Cinematic.aim_target(shot, pose, look.sample(u), shot.duration * progress), absolute = true,
					hour = hour, fov = lerpf(shot.fov, shot.fov_end if not is_nan(shot.fov_end) else shot.fov, u),
					fire = _film_fire_at(shot, fire_level, progress), lanterns = lit, wind = gust, focus = shot.focus,
					weather = shot.weather, weather_time = shot.duration * progress, weather_duration = shot.duration,
					wildlife_cue = shot.wildlife_cue, wildlife_time = shot.duration * progress,
					exposure = lerpf(shot.exposure, shot.exposure_end if not is_nan(shot.exposure_end) else shot.exposure, progress),
					focus_distance = lerpf(shot.focus_distance, shot.focus_distance_end if not is_nan(shot.focus_distance_end) else shot.focus_distance, smoothstep(0.25, 0.75, progress))})
			if not is_nan(shot.lantern_hour):
				lanterns_on = true
			fire_level = _film_fire_at(shot, fire_level, 1.0)
			if not is_nan(shot.wind_end):
				wind = shot.wind_end
	var saved := 0
	for vp in viewpoints:
		var name: String = vp.name
		# --shots= names a viewpoint, or a film shot whatever samples it carries.
		if not wanted.is_empty() and not wanted.has(name) and not wanted.has(vp.get("base", name)):
			continue
		Game.world.weather.apply_chapter(vp.get("weather", ""), vp.get("weather_time", 0.0), vp.get("weather_duration", 1.0), false)
		Game.world.hour = vp.get("hour", base_hour)
		if Game.camp.wildlife != null and vp.has("wildlife_cue"):
			Game.camp.wildlife.set_film_cue(vp.wildlife_cue, vp.wildlife_time)
		Game.world.exposure_scale = vp.get("exposure", 1.0)
		Game.camp.campsite.firepit.intensity = vp.get("fire", 1.0)
		Cinematic._set_lanterns(vp.get("lanterns", true))
		Game.world.wind_scale = vp.get("wind", 1.0)
		var absolute: bool = vp.get("absolute", false)
		camera.global_position = vp.pos if absolute else ground_relative(vp.pos)
		camera.look_at(vp.look if absolute else ground_relative(vp.look), Vector3.UP)
		camera.fov = vp.get("fov", 68.0)
		Game.world.reset_water_lens()
		var target: Vector3 = vp.look if absolute else ground_relative(vp.look)
		var focus_distance: float = vp.get("focus_distance", NAN)
		var tight := not is_nan(focus_distance)
		Cinematic.apply_focus(vp.get("focus", false), focus_distance if tight else camera.global_position.distance_to(target), tight)
		await _wait(SETTLE_SECONDS)
		await RenderingServer.frame_post_draw
		var img := get_viewport().get_texture().get_image()
		var path := "%s/%s.png" % [dir, name]
		var saved_error := img.save_png(path)
		if saved_error != OK:
			push_error("Screenshot write failed: " + path)
			Game.quit_cleanly(1)
			return
		saved += 1
		print("SHOT %s fps=%d" % [path, Engine.get_frames_per_second()])
		if Game.has_flag("dump-environment"):
			var env: Environment = Game.world.environment
			print("ENV ", JSON.stringify({shot = name, hour = Game.world.hour, underwater = Game.world.underwater,
				camera = str(camera.global_position), fog = str(env.fog_light_color), density = env.fog_density,
				volume_density = env.volumetric_fog_density, volume_albedo = str(env.volumetric_fog_albedo),
				history = env.volumetric_fog_temporal_reprojection_amount}))
		if Game.has_flag("dump-reflection") and Game.camp.pond != null:
			var refl: Image = Game.camp.pond.reflection_viewport.get_texture().get_image()
			refl.save_png("%s/%s_reflection.png" % [dir, name])
			Game.camp.pond.underwater_viewport.get_texture().get_image().save_png("%s/%s_underwater_reflection.png" % [dir, name])
			Game.camp.pond.transmission_viewport.get_texture().get_image().save_png("%s/%s_transmission.png" % [dir, name])
	Game.world.hour = base_hour
	print("CAPTURE_DONE images=%d" % saved)
	Game.quit_cleanly(0 if saved > 0 else 1)


## A coupled scene check: native boat responds to a push and a nearby impact,
## then settles. The sparse GPU probes and actual hull are sampled in motion.
func run_pond_check(dir: String) -> void:
	DirAccess.make_dir_recursive_absolute(dir)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var pond: Pond = Game.camp.pond
	var boat: Canoe = Game.camp.campsite.dock.canoe
	Game.player.set_physics_process(false)
	Game.player.global_position = Vector3(10000, 10, 10000)
	Game.world.hour = 17.8
	Game.world.wind_scale = 0.8
	camera.global_position = Vector3(-21.1, 0.6, 0.8)
	camera.look_at(boat.global_position + Vector3.UP * 0.20, Vector3.UP)
	camera.fov = 48.0
	await _wait(8.0)
	var original := boat.global_position
	get_viewport().get_texture().get_image().save_png(dir.path_join("boat-rest.png"))
	var before := pond.surface_time
	boat.apply_central_impulse(Vector3(-7.0, 1.0, 0.0))
	pond.ripple(boat.global_position + Vector3(0.6, 0, 0), 1.0)
	var maximum_heave := 0.0
	var maximum_drift := 0.0
	var maximum_tilt := 0.0
	var maximum_probe_age := 0.0
	var maximum_residual := 0.0
	var received_probe_sample := false
	var finite := true
	var image_taken := false
	while pond.surface_time - before < 16.0:
		await get_tree().process_frame
		var delta := boat.global_position - original
		maximum_heave = maxf(maximum_heave, absf(delta.y))
		maximum_drift = maxf(maximum_drift, Vector2(delta.x, delta.z).length())
		maximum_tilt = maxf(maximum_tilt, maxf(absf(boat.rotation.x), absf(boat.rotation.z)))
		finite = finite and boat.global_position.is_finite() and boat.linear_velocity.is_finite() \
			and boat.rotation.is_finite() and boat.angular_velocity.is_finite()
		if pond.simulation != null and pond.simulation.get_probe_sample_time() >= 0.0:
			received_probe_sample = true
			maximum_probe_age = maxf(maximum_probe_age, pond.simulation.get_simulation_time() - pond.simulation.get_probe_sample_time())
			for height in pond.simulation.get_probe_heights():
				finite = finite and is_finite(height)
				maximum_residual = maxf(maximum_residual, absf(height))
		if not image_taken and pond.surface_time - before >= 1.3:
			get_viewport().get_texture().get_image().save_png(dir.path_join("boat-disturbed.png"))
			image_taken = true
	get_viewport().get_texture().get_image().save_png(dir.path_join("boat-settled.png"))
	var report := {finite = finite, gpu_ready = pond.simulation != null and pond.simulation.is_ready(),
		max_heave_m = maximum_heave, max_drift_m = maximum_drift,
		max_tilt_degrees = rad_to_deg(maximum_tilt), max_probe_age_seconds = maximum_probe_age,
		received_probe_sample = received_probe_sample, max_sampled_residual_m = maximum_residual,
		settled_speed_m_s = boat.linear_velocity.length(), preset = Quality.current.display_name}
	var rid := get_viewport().get_viewport_rid()
	RenderingServer.viewport_set_measure_render_time(rid, true)
	var costs := {}
	for enabled: bool in [true, false, true]:
		pond.simulation_paused = not enabled
		RenderingServer.global_shader_parameter_set("pond_interaction_enabled", enabled)
		await _wait(1.2)
		var frames := 0.0
		var gpu := 0.0
		for sample in 60:
			await get_tree().process_frame
			frames += get_process_delta_time() * 1000.0
			gpu += RenderingServer.viewport_get_measured_render_time_gpu(rid)
		var label := "on_repeat" if enabled and costs.has("on") else "on" if enabled else "off"
		costs[label] = {frame_ms = frames / 60.0, main_viewport_gpu_ms = gpu / 60.0}
	report["interaction_comparison"] = costs
	var ok: bool = finite and report.gpu_ready and received_probe_sample and maximum_probe_age < 0.5 \
		and maximum_residual > 0.00001 and maximum_heave < 0.16 and maximum_drift < 0.65 \
		and maximum_tilt < 0.26 and report.settled_speed_m_s < 0.10
	report["passed"] = ok
	var file := FileAccess.open(dir.path_join("report.json"), FileAccess.WRITE)
	file.store_string(JSON.stringify(report, "\t"))
	print("POND_CHECK ", JSON.stringify(report))
	for view: Dictionary in _prop_viewpoints():
		camera.global_position = view.pos
		camera.look_at(view.look, Vector3.UP)
		await _wait(1.0)
		get_viewport().get_texture().get_image().save_png(dir.path_join(view.name + ".png"))
	if not ok:
		push_error("Coupled pond check failed")
		get_tree().quit(1)
	else:
		Game.quit_cleanly()


func _prop_viewpoints() -> Array[Dictionary]:
	var dock: Dock = Game.camp.campsite.dock
	var boat: Canoe = dock.canoe
	# Close views expose open stems, rim joints and seating that crosses a hull.
	var views: Array[Dictionary] = [
		{name = "boat-bow", pos = Vector3(0.75, 1.15, -3.35), look = Vector3(0, 0.16, -1.5)},
		{name = "boat-stern", pos = Vector3(-0.75, 1.15, 3.35), look = Vector3(0, 0.16, 1.5)},
		{name = "boat-interior", pos = Vector3(0.55, 2.3, 0.5), look = Vector3(0, 0.18, 0)},
	]
	for view in views:
		view.pos = boat.to_global(view.pos)
		view.look = boat.to_global(view.look)
		view.absolute = true
		view.fov = 48.0
		view.hour = 17.8
	var stones: Node3D = dock.get_node("SkippingStones")
	views.append({name = "skipping_stones", pos = dock.to_global(stones.position + Vector3(0.24, 0.38, 0.36)),
		look = stones.global_position - Vector3.UP * 0.04, absolute = true, fov = 48.0, hour = 17.8})
	return views


static func _film_fire_at(shot: Cinematic.Shot, start: float, progress: float) -> float:
	if not is_nan(shot.fire_end):
		return lerpf(start, shot.fire_end, smoothstep(0.0, 1.0, progress))
	var seconds := shot.duration * progress
	if shot.feed_fire and seconds >= shot.feed_at:
		var fed := minf(maxf(0.0, start - shot.feed_at * Firepit.DECAY_PER_SECOND) + Firepit.FEED_AMOUNT, Firepit.MAX_INTENSITY)
		return maxf(0.0, fed - (seconds - shot.feed_at) * Firepit.DECAY_PER_SECOND)
	return maxf(0.0, start - seconds * Firepit.DECAY_PER_SECOND)


func run_benchmark(presets: String, out_path: String) -> void:
	var tiers: Array[QualityPreset.Tier] = []
	if presets == "":
		tiers = Quality.TIER_ORDER.duplicate()
	else:
		for name in presets.split(",", false):
			tiers.append(Quality.tier_from_name(name.strip_edges().to_lower()))
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var results := {}
	var path := Spline.new(BENCH_PATH)
	var look := Spline.new(BENCH_LOOK)
	for tier in tiers:
		Quality.apply(tier)
		var preset := Quality.current
		# Measure the complete layout after a preset change, not its worker build.
		while Game.camp.understory._replant_thread != null:
			await get_tree().process_frame
		camera.global_position = ground_relative(path.sample(0.0))
		camera.look_at(ground_relative(look.sample(0.0)), Vector3.UP)
		await _wait(WARMUP_SECONDS)
		var frame_times := PackedFloat32Array()
		var elapsed := 0.0
		while elapsed < BENCH_SECONDS:
			var dt := get_process_delta_time()
			elapsed += dt
			var u := elapsed / BENCH_SECONDS
			camera.global_position = ground_relative(path.sample(u))
			camera.look_at(ground_relative(look.sample(u)), Vector3.UP)
			frame_times.append(dt * 1000.0)
			await get_tree().process_frame
		results[preset.display_name.to_lower()] = summarise(frame_times)
		print("BENCH %s: %s" % [preset.display_name, JSON.stringify(results[preset.display_name.to_lower()])])
	var file := FileAccess.open(out_path, FileAccess.WRITE)
	if file != null:
		var payload := {
			engine = Engine.get_version_info().string,
			gpu = RenderingServer.get_video_adapter_name(),
			resolution = "%dx%d" % [get_viewport().size.x, get_viewport().size.y],
			results = results,
		}
		file.store_string(JSON.stringify(payload, "  "))
		print("BENCH_WRITTEN ", ProjectSettings.globalize_path(out_path))
	print("BENCH_DONE")
	Game.quit_cleanly()


## `--profile`: measures the GPU cost of each renderer feature at the two
## heaviest viewpoints by switching features off one at a time.
func run_profile(out_path: String) -> void:
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var rid := get_viewport().get_viewport_rid()
	RenderingServer.viewport_set_measure_render_time(rid, true)
	var world: WorldController = Game.world
	var camp: Camp = Game.camp
	var scene := get_tree().current_scene
	var floor_dressing := scene.get_node_or_null("ForestFloorDressing")
	var biome_dressing := scene.get_node_or_null("BiomeDressing")
	var habitat := camp.get_node_or_null("HabitatDiversity")
	var drips := camp.get_node_or_null("CanopyDrips")
	_profile_snapshot(scene)
	_profile_refl_lod = camp.pond.reflection_viewport.mesh_lod_threshold
	_profile_refl_far = camp.pond.reflection_camera.far
	_profile_fire_casters = camp.campsite.firepit.light.shadow_caster_mask
	_profile_sun_casters = world.sun.shadow_caster_mask
	var cases: Array[Dictionary] = [
		{name = "all on", apply = func() -> void: pass},
		{name = "sdfgi off", apply = func() -> void: world.environment.sdfgi_enabled = false},
		{name = "ssil off", apply = func() -> void: world.environment.ssil_enabled = false},
		{name = "ssao off", apply = func() -> void: world.environment.ssao_enabled = false},
		{name = "volumetric off", apply = func() -> void: world.environment.volumetric_fog_enabled = false},
		{name = "ssr off", apply = func() -> void: world.environment.ssr_enabled = false},
		{name = "glow off", apply = func() -> void: world.environment.glow_enabled = false},
		{name = "planar refl off", apply = func() -> void: camp.pond.planar_enabled = false},
		{name = "sun shadow off", apply = func() -> void: world.sun.shadow_enabled = false},
		{name = "soft shadows low", apply = func() -> void:
			RenderingServer.directional_soft_shadow_filter_set_quality(RenderingServer.SHADOW_QUALITY_SOFT_LOW)
			RenderingServer.positional_soft_shadow_filter_set_quality(RenderingServer.SHADOW_QUALITY_SOFT_LOW)},
		{name = "shadow atlas 2048", apply = func() -> void: RenderingServer.directional_shadow_atlas_set_size(2048, true)},
		{name = "shadow 2 splits", apply = func() -> void: world.sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS},
		{name = "shadow dist 60", apply = func() -> void: world.sun.directional_shadow_max_distance = 60.0},
		{name = "fire shadow off", apply = func() -> void: camp.campsite.firepit.light.shadow_enabled = false},
		{name = "lantern shadows off", apply = func() -> void:
			for l in camp.campsite.lanterns:
				l.light.shadow_enabled = false},
		{name = "understory hidden", apply = func() -> void: camp.understory.visible = false},
		{name = "grass hidden", apply = func() -> void: _profile_hide(camp.understory, ["Grass_", "HillGrass_"])},
		{name = "near grass hidden", apply = func() -> void: _profile_hide(camp.understory, ["Grass_"])},
		{name = "hill grass hidden", apply = func() -> void: _profile_hide(camp.understory, ["HillGrass_"])},
		{name = "ferns hidden", apply = func() -> void: _profile_hide(camp.understory, ["Ferns"])},
		{name = "shrubs hidden", apply = func() -> void:
			_profile_hide(camp.understory, ["Shrubs", "HillShrubs_"])
			if biome_dressing != null:
				_profile_hide(biome_dressing, ["WetBankShrubs"])},
		{name = "meadow hidden", apply = func() -> void: _profile_hide(camp.understory, ["MeadowTussocks_", "MeadowSeedHeads_"])},
		{name = "habitat hidden", apply = func() -> void:
			if habitat != null:
				habitat.visible = false},
		{name = "floor dressing hidden", apply = func() -> void:
			if floor_dressing != null:
				floor_dressing.visible = false},
		{name = "biome dressing hidden", apply = func() -> void:
			if biome_dressing != null:
				biome_dressing.visible = false},
		{name = "scanned hidden", apply = func() -> void:
			var scanned := camp.get_node_or_null("ScannedDressing")
			if scanned != null:
				scanned.visible = false},
		{name = "drips hidden", apply = func() -> void:
			if drips != null:
				drips.visible = false},
		{name = "ridges hidden", apply = func() -> void: camp.ridge_forest.visible = false},
		{name = "forest hidden", apply = func() -> void: camp.forest.visible = false},
		{name = "near trees hidden", apply = func() -> void:
			for n in camp.forest.get_children():
				if n is GeometryInstance3D and not n.name.begins_with("FarTrees_"):
					n.visible = false},
		{name = "far trees hidden", apply = func() -> void: _profile_hide(camp.forest, ["FarTrees_"])},
		{name = "veg shadows off", apply = func() -> void:
			_profile_no_shadows(camp.understory)
			if floor_dressing != null:
				_profile_no_shadows(floor_dressing)
			if biome_dressing != null:
				_profile_no_shadows(biome_dressing)},
		{name = "forest shadows off", apply = func() -> void: _profile_no_shadows(camp.forest)},
		{name = "far tree shadows off", apply = func() -> void:
			for n in camp.forest.get_children():
				if n is GeometryInstance3D and n.name.begins_with("FarTrees_"):
					n.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF},
		{name = "fire fx hidden", apply = func() -> void:
			for n in [camp.campsite.firepit.flame_volume, camp.campsite.firepit.sparks, camp.campsite.firepit.smoke, camp.campsite.firepit.haze]:
				n.visible = false},
		{name = "fire shadow parab", apply = func() -> void:
			camp.campsite.firepit.light.omni_shadow_mode = OmniLight3D.SHADOW_DUAL_PARABOLOID},
		{name = "fire casters no grass", apply = func() -> void:
			camp.campsite.firepit.light.shadow_caster_mask = 0xFFFFF & ~Pond.GRASS_LAYER},
		{name = "sun casters no grass", apply = func() -> void:
			world.sun.shadow_caster_mask = 0xFFFFF & ~Pond.GRASS_LAYER},
		{name = "refl lod 8px", apply = func() -> void: camp.pond.reflection_viewport.mesh_lod_threshold = 8.0},
		{name = "refl far 250", apply = func() -> void: camp.pond.reflection_camera.far = 250.0},
		{name = "leaves hidden", apply = func() -> void:
			for n in camp.forest.get_children():
				if n is GeometryInstance3D and n.name.ends_with("_leaves") or n.name == "FarTrees_leaves":
					n.visible = false},
		{name = "bark hidden", apply = func() -> void:
			for n in camp.forest.get_children():
				if n is GeometryInstance3D and n.name.ends_with("_bark") or n.name == "FarTrees_bark":
					n.visible = false},
		{name = "terrain hidden", apply = func() -> void: camp.terrain.visible = false},
		{name = "overdraw shot", apply = func() -> void: get_viewport().debug_draw = Viewport.DEBUG_DRAW_OVERDRAW},
		{name = "impostors off", apply = func() -> void: camp.forest.set_switch_distance(100000.0)},
		{name = "impostors at 30 m", apply = func() -> void: camp.forest.set_switch_distance(30.0)},
		{name = "leaf lod old", apply = func() -> void: camp.forest.set_leaf_lod([45.0, 120.0, 0.48, 0.12], [45.0, 120.0, 0.48, 0.12], Quality.current.foliage_distance)},
		{name = "leaf lod strong", apply = func() -> void: camp.forest.set_leaf_lod([24.0, 80.0, 0.78, 0.6], [30.0, 90.0, 0.88, 1.0], Quality.current.foliage_distance)},
		{name = "grass lod bias 0.5", apply = func() -> void: _profile_lod_bias(camp.understory, ["Grass_", "HillGrass_", "MeadowTussocks_"], 0.5)},
		{name = "grass lod bias 0.25", apply = func() -> void: _profile_lod_bias(camp.understory, ["Grass_", "HillGrass_", "MeadowTussocks_"], 0.25)},
		{name = "tree lod bias 0.5", apply = func() -> void: _profile_lod_bias(camp.forest, [""], 0.5)},
		{name = "tree lod bias 0.25", apply = func() -> void: _profile_lod_bias(camp.forest, [""], 0.25)},
		{name = "taa off", apply = func() -> void: get_viewport().use_taa = false},
		{name = "fsr2 77%", apply = func() -> void:
			get_viewport().scaling_3d_mode = Viewport.SCALING_3D_MODE_FSR2
			get_viewport().scaling_3d_scale = 0.77},
	]
	var report := {}
	var wanted := Game.arg_value("profile-cases", "").split(",", false)
	var views := Game.arg_value("profile-views", "fire,pond").split(",", false)
	for vp_name in views:
		var matches := VIEWPOINTS.filter(func(v: Dictionary) -> bool: return v.name == vp_name)
		if matches.is_empty():
			push_warning("Unknown profile viewpoint %s" % vp_name)
			continue
		var vp: Dictionary = matches[0]
		camera.global_position = ground_relative(vp.pos)
		camera.look_at(ground_relative(vp.look), Vector3.UP)
		print("PROFILE viewpoint %s" % vp_name)
		var rows := {}
		for c in cases:
			if not wanted.is_empty() and not wanted.has(c.name):
				continue
			var apply: Callable = c.apply
			apply.call()
			await _wait(1.2)
			var gpu := 0.0
			var cpu := 0.0
			var frame := 0.0
			var samples := 45
			for i in samples:
				await get_tree().process_frame
				gpu += RenderingServer.viewport_get_measured_render_time_gpu(rid)
				cpu += RenderingServer.viewport_get_measured_render_time_cpu(rid)
				frame += get_process_delta_time() * 1000.0
			gpu /= float(samples)
			cpu /= float(samples)
			frame /= float(samples)
			if c.name == "overdraw shot":
				get_viewport().get_texture().get_image().save_png(out_path.get_base_dir().path_join("overdraw_%s.png" % vp_name))
			var objects := RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TOTAL_OBJECTS_IN_FRAME)
			var primitives := RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TOTAL_PRIMITIVES_IN_FRAME)
			var draw_calls := RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TOTAL_DRAW_CALLS_IN_FRAME)
			rows[c.name] = {gpu_ms = snappedf(gpu, 0.01), cpu_ms = snappedf(cpu, 0.01), frame_ms = snappedf(frame, 0.01),
				objects = objects, primitives = primitives, draw_calls = draw_calls}
			print("PROFILE %-22s gpu %6.2f ms   cpu %6.2f ms   frame %6.2f ms   objects %6d   tris %9d   draws %5d" % [c.name, gpu, cpu, frame, objects, primitives, draw_calls])
			_restore_profile_state()
			await _wait(0.4)
		report[vp_name] = rows
	var file := FileAccess.open(out_path, FileAccess.WRITE)
	if file != null:
		file.store_string(JSON.stringify(report, "  "))
	print("PROFILE_DONE")
	Game.quit_cleanly()


var _profile_state: Dictionary = {}
var _profile_refl_lod := 1.0
var _profile_refl_far := 700.0
var _profile_fire_casters := 0xFFFFF
var _profile_sun_casters := 0xFFFFF


## Remember visibility and shadow casting of every geometry node so each
## profile case starts from the same scene.
func _profile_snapshot(root: Node) -> void:
	_profile_state.clear()
	var stack: Array[Node] = [root]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		if n is Node3D:
			var entry := {visible = (n as Node3D).visible}
			if n is GeometryInstance3D:
				entry["cast_shadow"] = (n as GeometryInstance3D).cast_shadow
				entry["lod_bias"] = (n as GeometryInstance3D).lod_bias
			_profile_state[n] = entry
		for c in n.get_children():
			stack.append(c)


func _profile_hide(parent: Node, prefixes: Array) -> void:
	for n in parent.get_children():
		if not (n is Node3D):
			continue
		for prefix: String in prefixes:
			if n.name.begins_with(prefix):
				(n as Node3D).visible = false
				break


func _profile_lod_bias(parent: Node, prefixes: Array, bias: float) -> void:
	for n in parent.get_children():
		if not (n is GeometryInstance3D):
			continue
		for prefix: String in prefixes:
			if n.name.begins_with(prefix):
				(n as GeometryInstance3D).lod_bias = bias
				break


func _profile_no_shadows(root: Node) -> void:
	var stack: Array[Node] = [root]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		if n is GeometryInstance3D:
			(n as GeometryInstance3D).cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		for c in n.get_children():
			stack.append(c)


func _restore_profile_state() -> void:
	for n: Node in _profile_state:
		if not is_instance_valid(n):
			continue
		var entry: Dictionary = _profile_state[n]
		(n as Node3D).visible = entry.visible
		if entry.has("cast_shadow"):
			(n as GeometryInstance3D).cast_shadow = entry.cast_shadow
			(n as GeometryInstance3D).lod_bias = entry.lod_bias
	var camp: Camp = Game.camp
	if camp.campsite != null and camp.campsite.firepit != null:
		camp.campsite.firepit.light.shadow_enabled = true
		camp.campsite.firepit.light.omni_shadow_mode = OmniLight3D.SHADOW_CUBE
		for l in camp.campsite.lanterns:
			l.light.shadow_enabled = Quality.current.lantern_shadows
	camp.pond.planar_enabled = Quality.current.planar_reflections
	camp.forest.set_switch_distance(-1.0)
	camp.pond.reflection_viewport.mesh_lod_threshold = _profile_refl_lod
	camp.pond.reflection_camera.far = _profile_refl_far
	camp.campsite.firepit.light.shadow_caster_mask = _profile_fire_casters
	Game.world.sun.shadow_enabled = true
	Game.world.sun.shadow_caster_mask = _profile_sun_casters
	get_viewport().debug_draw = Viewport.DEBUG_DRAW_DISABLED
	Quality.apply(Quality.current.tier)


## Viewpoint heights are authored relative to the ground under them.
static func ground_relative(p: Vector3) -> Vector3:
	var ground: float = Game.camp.height_at(p.x, p.z)
	var water := TerrainField.WATER_LEVEL
	return Vector3(p.x, maxf(ground, water) + p.y, p.z)


## Frame-time statistics in milliseconds: mean, median, 1% worst and derived fps.
static func summarise(frame_ms: PackedFloat32Array) -> Dictionary:
	if frame_ms.is_empty():
		return {frames = 0}
	var sorted := frame_ms.duplicate()
	sorted.sort()
	var total := 0.0
	for f in sorted:
		total += f
	var mean := total / float(sorted.size())
	var p50 := sorted[int(float(sorted.size()) * 0.5)]
	var p99 := sorted[mini(int(float(sorted.size()) * 0.99), sorted.size() - 1)]
	return {
		frames = sorted.size(),
		mean_ms = snappedf(mean, 0.01),
		median_ms = snappedf(p50, 0.01),
		p99_ms = snappedf(p99, 0.01),
		fps = snappedf(1000.0 / maxf(mean, 1e-3), 0.1),
		low_1pct_fps = snappedf(1000.0 / maxf(p99, 1e-3), 0.1),
	}


## Waits for the time and for enough frames: a hidden window can run at one
## frame per second, and GI and the temporal volumetrics settle per frame.
func _wait(seconds: float, minimum_frames := 90) -> void:
	var t := 0.0
	var frames := 0
	while t < seconds or frames < minimum_frames:
		t += get_process_delta_time()
		frames += 1
		await get_tree().process_frame
