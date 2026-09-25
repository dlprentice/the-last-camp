class_name Cinematic
extends Node

## Scripted camera sequences for recording with Godot's Movie Maker mode:
##   godot --write-movie out.avi --fixed-fps 60 --fullscreen -- --cinematic=arrival
## Each shot moves the camera along an eased spline, aims it along a second
## one, and may set or time-lapse the hour, change the field of view, focus the
## depth of field and feed the fire. Each shot defines its cuts or fades; a
## sequence opens on a title card and quits after the end card. Rendering runs at Film quality with
## supersampling: in movie mode every frame is written, whatever it costs.

class Shot:
	var label := ""
	## One line naming the rendering features on screen, shown under the label.
	var caption := ""
	var path: Array[Vector3] = []
	var look: Array[Vector3] = []
	var duration := 8.0
	var fov := 50.0
	var fov_end := NAN
	var hour := NAN
	var hour_end := NAN
	var absolute := false
	var fade_in := 0.7
	var fade_out := 0.7
	var focus := false
	var feed_fire := false
	var skip_stone := false
	var feed_at := 0.0
	var fire := NAN
	var lantern_state := -1
	var lantern_hour := NAN
	var clock := false
	var interior := false
	var wind := NAN
	var wind_end := NAN
	var hold_start := 0.0
	var hold_end := 0.0
	var ramp_seconds := 0.0
	var rest_at := 0.5
	var rest_seconds := 0.0
	var stone_at := 1.4
	var stone_origin := Vector3.INF
	var stone_direction := Vector3.ZERO
	var fire_end := NAN
	# Walking is opt-in: grounded eye height and distance-driven footsteps.
	# Close observations, floating and seated shots retain their authored pose.
	var walk := false
	var eye_height := 1.64
	var walk_surface: StringName = &"dirt"
	var wildlife_cue: StringName = &""
	var exposure := 1.0
	var exposure_end := NAN
	var continuous_in := false
	var focus_distance := NAN
	var focus_distance_end := NAN
	var weather := ""
	# Seconds, pitch in degrees, blend weight. Local framing can leave the
	# entry, composed hold and ending aim of the base spline untouched.
	var pitch_envelope: Array[Vector3] = []
	var pitch_ramp_seconds := 0.2

## Internal render scale for 1080p movies. At `--movie-size` resolutions the
## frame is already large, so it renders native instead.
const SUPERSAMPLE := 1.5
const TITLE_SECONDS := 5.0
## Closing card plus a movie-style credit roll; the roll's length depends on
## the sequence (see end_seconds), the default covers the short studies.
const END_SECONDS := 25.0
const CARD_SECONDS := 5.0

var camera: Camera3D
var sequence_name := ""

var _shots: Array[Shot] = []
var _max_seconds := 0.0
var _end_seconds := END_SECONDS
var _roll_compact := false
var _roll: VBoxContainer
var _roll_height := 0.0
var _serif: SystemFont
var _total_elapsed := 0.0
var _frames_dir := ""
var _frames_saved := 0
var _save_tasks: Array[int] = []
const SAVE_TASKS_IN_FLIGHT := 20
var _index := -1
var _elapsed := 0.0
var _phase := "title"
var _stone_cued := false
var _fire_cued := false
var _lanterns_cued := false
var _shot_fire_start := 1.0
var _walk_step := 0
var _path: Spline
var _look: Spline
var _overlay: CanvasLayer
var _black: ColorRect
var _title: Label
var _subtitle: Label
var _end_title: Label
var _end_note: Label
var _credits_page := -1
var _clock: Label
var _caption_title: Label
var _caption: Label


func _ready() -> void:
	# Water masks and planar reflections must read this frame's camera pose.
	process_priority = -10
	camera = Camera3D.new()
	camera.name = "CinematicCamera"
	camera.near = 0.05
	camera.far = 1600.0
	camera.cull_mask = 0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)
	add_child(camera)
	_build_overlay()


func _build_overlay() -> void:
	_overlay = CanvasLayer.new()
	_overlay.layer = 30
	add_child(_overlay)
	_black = ColorRect.new()
	_black.color = Color.BLACK
	_black.set_anchors_preset(Control.PRESET_FULL_RECT)
	_black.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_overlay.add_child(_black)
	_title = UiTheme.label("THE LAST CAMP", 64, Color(0.93, 0.89, 0.79))
	var serif := SystemFont.new()
	serif.font_names = PackedStringArray(["Noto Serif", "DejaVu Serif"])
	_title.add_theme_font_override("font", serif)
	_title.add_theme_constant_override("outline_size", 0)
	_place(_title, Vector2(-600, -70), Vector2(600, 30))
	_subtitle = UiTheme.label("A F T E R G L O W", 18, UiTheme.MUTED)
	_place(_subtitle, Vector2(-600, 40), Vector2(600, 80))
	_end_title = UiTheme.label("Stay a little longer.", 46, Color(0.93, 0.89, 0.79))
	_end_title.add_theme_font_override("font", serif)
	_place(_end_title, Vector2(-600, -60), Vector2(600, 20))
	_end_note = UiTheme.label("THE LAST CAMP\nCaptured in Godot 4.8", 18, UiTheme.MUTED)
	_place(_end_note, Vector2(-600, 30), Vector2(600, 110))
	for label in [_title, _subtitle, _end_title, _end_note]:
		label.modulate.a = 0.0
	_clock = UiTheme.label("", 23, Color(0.87, 0.86, 0.79, 0.85))
	_clock.position = Vector2(58, 994)
	_clock.visible = false
	_overlay.add_child(_clock)
	# Lower-third for each shot: its name and the features on screen.
	_caption_title = UiTheme.label("", 27, Color(0.93, 0.89, 0.79), HORIZONTAL_ALIGNMENT_LEFT)
	_caption_title.add_theme_font_override("font", serif)
	_caption_title.add_theme_constant_override("outline_size", 0)
	_caption_title.position = Vector2(58, 918)
	_caption_title.visible = false
	_overlay.add_child(_caption_title)
	_caption = UiTheme.label("", 20, Color(0.80, 0.81, 0.84, 0.95), HORIZONTAL_ALIGNMENT_LEFT)
	_caption.add_theme_constant_override("outline_size", 0)
	_caption.position = Vector2(58, 958)
	_caption.visible = false
	_overlay.add_child(_caption)


func _place(c: Control, top_left: Vector2, bottom_right: Vector2) -> void:
	c.anchor_left = 0.5
	c.anchor_right = 0.5
	c.anchor_top = 0.5
	c.anchor_bottom = 0.5
	c.offset_left = top_left.x
	c.offset_top = top_left.y
	c.offset_right = bottom_right.x
	c.offset_bottom = bottom_right.y
	if c.get_parent() == null:
		_black.add_child(c)


## Starts a named sequence (see `sequence`). Forces Film quality + supersampling and
## a sharper, full-rate mirror, hides the HUD and takes the camera.
func play(name: String) -> void:
	sequence_name = name
	_subtitle.text = "A F T E R G L O W" if name == "afterglow" else name.to_upper()
	if name == "one_night":
		_subtitle.text = "O N E  N I G H T"
		_end_note.text = "Captured in Godot 4.8"
	_shots = sequence(name)
	# Inspect selected complete shots at their real pace before a full export.
	# The delivered film omits this review-only argument.
	if Game.has_flag("cinematic-shots"):
		var selected: Array[Shot] = []
		for value in Game.arg_value("cinematic-shots", "").split(",", false):
			if not value.is_valid_int() or int(value) < 0 or int(value) >= _shots.size():
				push_error("cinematic-shots expects valid zero-based shot indices")
				get_tree().quit(1)
				return
			selected.append(_shots[int(value)])
		_shots = selected
	if _shots.is_empty():
		push_error("Cinematic: unknown sequence '%s'" % name)
		get_tree().quit(1)
		return
	Quality.explicit = true
	Quality.apply(Quality.tier_from_name(Game.arg_value("capture-quality", "ultra")))
	var large_frame := Game.has_flag("movie-size")
	if not Game.has_flag("capture-quality"):
		get_viewport().scaling_3d_scale = 1.0 if large_frame else SUPERSAMPLE
	if not Game.has_flag("capture-quality") and Game.camp != null and Game.camp.pond != null:
		Game.camp.pond.set_reflection_scale(0.65 if large_frame else 1.0)
	_max_seconds = float(Game.arg_value("max-seconds", "0"))
	_end_seconds = end_seconds(name)
	_roll_compact = name != "one_night"
	if Game.has_flag("credits-only"):
		_shots.clear()
	# `--frames-dir=DIR` writes every rendered frame of the root viewport as a
	# JPEG, at the viewport's own size (so 4K with --movie-size even though the
	# window cannot be), to be muxed with the movie writer's audio track.
	_frames_dir = Game.arg_value("frames-dir", "")
	if _frames_dir != "":
		DirAccess.make_dir_recursive_absolute(_frames_dir)
		RenderingServer.frame_post_draw.connect(_save_frame)
	# The cards are laid out in 1080p pixels; scale the overlay to the frame,
	# with the black backing sized in pre-scale units so its centre (where the
	# labels anchor) stays the frame's centre.
	var frame_size := get_viewport().get_visible_rect().size
	var overlay_scale := frame_size.y / 1080.0
	_overlay.scale = Vector2.ONE * overlay_scale
	_black.set_anchors_preset(Control.PRESET_TOP_LEFT)
	_black.position = Vector2.ZERO
	_black.size = frame_size / overlay_scale
	Game.hud_visible = false
	Game.mode = Game.Mode.PHOTO
	# The unattended player must not leave a stationary trample disc in the
	# meadow while this camera moves through it.
	if Game.player != null:
		Game.player.set_physics_process(false)
		Game.player.global_position = Vector3(10000, 10, 10000)
	camera.make_current()
	if Game.world != null:
		camera.attributes = Game.world.attributes
	_phase = "title"
	_elapsed = 0.0
	_index = -1
	# Resolve the opening view under the card so the mirror, shadows and GI
	# have the full title duration to settle before the first image appears.
	if not _shots.is_empty():
		var first := _shots[0]
		camera.global_position = _resolve(first.path, first.absolute)[0]
		camera.look_at(_resolve(first.look, first.absolute)[0], Vector3.UP)
		camera.fov = first.fov
		if Game.world != null and not is_nan(first.hour):
			Game.world.hour = first.hour
		if first.lantern_state >= 0:
			_set_lanterns(first.lantern_state == 1)
	if Game.camp.wildlife != null:
		Game.camp.wildlife.set_film_cue(&"", 0.0)
	if Game.audio != null:
		Game.audio.begin_film()
	print("CINEMATIC_START sequence=%s frames_drawn=%d shots=%d" % [name, Engine.get_frames_drawn(), _shots.size()])
	if Game.has_flag("skip-cards"):
		_next_shot()
	set_process(true)


func _process(delta: float) -> void:
	_elapsed += delta
	_total_elapsed += delta
	if _max_seconds > 0.0 and _total_elapsed >= _max_seconds:
		_finish()
		return
	match _phase:
		"title":
			_black.color.a = 1.0
			var a := smoothstep(0.0, 0.6, _elapsed) * (1.0 - smoothstep(TITLE_SECONDS - 0.6, TITLE_SECONDS, _elapsed))
			_title.modulate.a = a
			_subtitle.modulate.a = a
			if _elapsed >= TITLE_SECONDS:
				_next_shot()
		"shot":
			_update_shot()
		"end":
			_black.color.a = 1.0
			_update_credits()
			if Game.audio != null:
				# The ambience settles under the roll and leaves with the last card.
				var bed := 1.0 - 0.65 * smoothstep(0.2, 9.0, _elapsed)
				Game.audio.film_fade(bed * (1.0 - smoothstep(_end_seconds - 4.0, _end_seconds - 0.4, _elapsed)))
			if _elapsed >= _end_seconds:
				_finish()


func _update_credits() -> void:
	if _elapsed < CARD_SECONDS:
		var alpha := smoothstep(0.0, 0.6, _elapsed) * (1.0 - smoothstep(CARD_SECONDS - 0.6, CARD_SECONDS, _elapsed))
		_end_title.modulate.a = alpha
		_end_note.modulate.a = alpha
		return
	_end_title.modulate.a = 0.0
	_end_note.modulate.a = 0.0
	if _roll == null:
		_build_credit_roll(_roll_compact)
	# Constant speed from below the frame to fully above it, finishing a
	# breath before the film ends.
	var travel := _roll_height + 1080.0 + 60.0
	var seconds := maxf(_end_seconds - CARD_SECONDS - 1.5, 1.0)
	_roll.position.y = 1080.0 - (_elapsed - CARD_SECONDS) * travel / seconds


## Movie-style credits built from the three source tables so every creator
## and licence is on screen; the film is posted without its sidecar files.
func _build_credit_roll(compact: bool) -> void:
	_roll = VBoxContainer.new()
	_roll.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_roll.add_theme_constant_override("separation", 6)
	_roll.custom_minimum_size = Vector2(1500.0, 0.0)
	_roll.position = Vector2(210.0, 1080.0)
	_black.add_child(_roll)
	_roll_line("THE LAST CAMP", 54, Color(0.93, 0.89, 0.79), true)
	_roll_line("A rendering tech demo made with Godot 4.8", 24, UiTheme.MUTED)
	_roll_gap(70)
	_roll_header("Created by")
	_roll_line("David Prentice", 30, UiTheme.TEXT)
	_roll_gap(50)
	_roll_header("Rendered in")
	_roll_line("Godot Engine 4.8  ·  Forward+  ·  Vulkan", 24, UiTheme.TEXT)
	_roll_line("Movie Maker mode, 1920x1080 at 60 frames per second", 22, UiTheme.MUTED)
	_roll_gap(50)
	_roll_header("Everything grown by the project")
	_roll_line("Terrain, trees, understory, water, sky, weather, camp and wildlife", 22, UiTheme.MUTED)
	_roll_gap(50)
	_roll_header("Surfaces")
	_roll_line("Photoscanned by Poly Haven  ·  CC0 1.0", 22, UiTheme.MUTED)
	var surfaces := _credit_rows("res://textures/SOURCES.md", 1, 2, 4)
	if surfaces.is_empty():
		surfaces = [["Leafy Grass, Jolcham Oak Bark 01", "Charlotte Baglioni"], ["Forest Leaves 02, Rough Wood", "Rob Tuytel"],
			["Mud Forest, Stony Dirt Path", "eye-candy.xyz"], ["Lichen Rock", "Rico Cilliers"], ["Pine Bark", "Dimitrios Savva"], ["Fabric 061", "ambientCG  ·  CC0 1.0"]]
	for row in surfaces:
		_roll_pair(row[0], row[1])
	_roll_gap(50)
	_roll_header("Photoscanned models")
	_roll_line("Poly Haven  ·  CC0 1.0", 22, UiTheme.MUTED)
	var models := _credit_rows("res://models/SOURCES.md", 1, 2, 4)
	if models.is_empty():
		models = [["Stumps, trunks, roots, rocks, plants, saplings and camp props", "Rico Cilliers, Rob Tuytel, Jenelle van Heerden, James Ray Cock, Kless Gyzen, Kuutti Siitonen, Dario Barresi, Ulan Cabanilla, Alex Weber"]]
	if compact:
		var i := 0
		while i < models.size():
			var left: String = models[i][0] + "  ·  " + models[i][1]
			var right: String = (models[i + 1][0] + "  ·  " + models[i + 1][1]) if i + 1 < models.size() else ""
			_roll_pair(left, right, 20)
			i += 2
	else:
		for row in models:
			_roll_pair(row[0], row[1])
	_roll_gap(50)
	_roll_header("Sound")
	var sounds := _audio_credit_rows()
	if sounds.is_empty():
		sounds = [["Water Splash", "Mike Koenig  ·  SoundBible  ·  CC BY 3.0"], ["Water Churning, Thunder HD", "Mark DiAngelo  ·  SoundBible  ·  CC BY 3.0"],
			["Rain", "Ylmir  ·  OpenGameArt  ·  CC0 1.0"], ["Fireplace", "PagDev  ·  OpenGameArt  ·  CC0 1.0"], ["Trees in Wind", "naturenotesuk  ·  Freesound  ·  CC0 1.0"]]
	for row in sounds:
		_roll_pair(row[0], row[1])
	_roll_line("Recordings edited and mixed for the film", 20, UiTheme.MUTED)
	_roll_line("All other sound generated by the project  ·  no music", 20, UiTheme.MUTED)
	_roll_gap(80)
	_roll_line("Thank you for watching", 34, Color(0.93, 0.89, 0.79), true)
	_roll_gap(40)
	_roll_line("github.com/dlprentice/the-last-camp", 20, UiTheme.MUTED)
	_roll_height = _roll.get_combined_minimum_size().y


func _roll_font() -> SystemFont:
	if _serif == null:
		_serif = SystemFont.new()
		_serif.font_names = PackedStringArray(["Noto Serif", "DejaVu Serif"])
	return _serif


func _roll_line(text: String, size: int, color: Color, serif := false) -> void:
	var l := UiTheme.label(text, size, color)
	if serif:
		l.add_theme_font_override("font", _roll_font())
	l.add_theme_constant_override("outline_size", 0)
	_roll.add_child(l)


func _roll_header(text: String) -> void:
	_roll_gap(10)
	var l := UiTheme.label(text.to_upper(), 22, UiTheme.ACCENT)
	l.add_theme_font_override("font", _roll_font())
	l.add_theme_constant_override("outline_size", 0)
	_roll.add_child(l)
	_roll_gap(4)


func _roll_pair(left: String, right: String, size := 24) -> void:
	var row := HBoxContainer.new()
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_theme_constant_override("separation", 40)
	var a := UiTheme.label(left, size, UiTheme.MUTED, HORIZONTAL_ALIGNMENT_RIGHT)
	a.custom_minimum_size = Vector2(730.0, 0.0)
	a.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var b := UiTheme.label(right, size, UiTheme.TEXT, HORIZONTAL_ALIGNMENT_LEFT)
	b.custom_minimum_size = Vector2(730.0, 0.0)
	b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for l in [a, b]:
		l.add_theme_constant_override("outline_size", 0)
		row.add_child(l)
	_roll.add_child(row)


func _roll_gap(pixels: int) -> void:
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0.0, float(pixels))
	spacer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_roll.add_child(spacer)


## Rows of a markdown table as [name, creator (with a licence note when it is
## not CC0)] from the given column indices; empty when the file is absent.
static func _credit_rows(path: String, name_column: int, creator_column: int, licence_column: int) -> Array:
	var rows := []
	for cells in _table_cells(path):
		if cells.size() <= maxi(maxi(name_column, creator_column), licence_column):
			continue
		var name := _link_text(cells[name_column])
		if name.find("_") != -1 or name == name.to_lower():
			name = name.replace("_", " ").capitalize()
		var creator := _strip_parentheticals(_link_text(cells[creator_column]))
		var licence := _link_text(cells[licence_column])
		if not licence.begins_with("CC0"):
			creator += "  ·  " + licence
		rows.append([name, creator])
	return rows


static func _audio_credit_rows() -> Array:
	var rows := []
	var seen := {}
	for cells in _table_cells("res://audio/SOURCES.md"):
		if cells.size() < 3:
			continue
		var entry := _link_text(cells[1])
		var url := _link_url(cells[1])
		var parts := entry.split(" — ", false)
		if parts.size() < 2:
			continue
		var title := _strip_parentheticals(parts[0])
		if seen.has(title):
			continue
		seen[title] = true
		var provider := "SoundBible" if url.find("soundbible") != -1 else ("OpenGameArt" if url.find("opengameart") != -1 else ("Freesound" if url.find("freesound") != -1 else ""))
		var licence := _link_text(cells[2]).replace("CC0", "CC0 1.0").replace("CC0 1.0 1.0", "CC0 1.0")
		# "Mike Koenig" or "Ylmir, take 3": the take note belongs to the sidecar.
		var credit := parts[1].split(",", false)[0].strip_edges()
		if provider != "":
			credit += "  ·  " + provider
		credit += "  ·  " + licence
		rows.append([title, credit])
	return rows


static func _table_cells(path: String) -> Array:
	var out := []
	if not FileAccess.file_exists(path):
		return out
	var text := FileAccess.get_file_as_string(path)
	var header_seen := false
	for line in text.split("\n"):
		var trimmed: String = line.strip_edges()
		if not trimmed.begins_with("|"):
			continue
		if trimmed.begins_with("|---") or trimmed.begins_with("| ---"):
			header_seen = true
			continue
		if not header_seen:
			continue
		var cells := []
		for cell in trimmed.trim_prefix("|").trim_suffix("|").split("|"):
			cells.append((cell as String).strip_edges())
		out.append(cells)
	return out


static func _link_text(cell: String) -> String:
	var regex := RegEx.new()
	regex.compile("\\[([^\\]]+)\\]\\([^)]*\\)")
	return regex.sub(cell, "$1", true).replace("`", "").strip_edges()


static func _link_url(cell: String) -> String:
	var regex := RegEx.new()
	regex.compile("\\]\\(([^)]*)\\)")
	var m := regex.search(cell)
	return m.get_string(1) if m != null else ""


static func _strip_parentheticals(text: String) -> String:
	var regex := RegEx.new()
	regex.compile("\\s*\\([^)]*\\)")
	return regex.sub(text, "", true).strip_edges()


func _finish() -> void:
	set_process(false)
	print("CINEMATIC_DONE frames_drawn=%d frames_saved=%d viewport=%s" % [Engine.get_frames_drawn(), _frames_saved, get_viewport().get_visible_rect().size])
	if _frames_dir != "":
		RenderingServer.frame_post_draw.disconnect(_save_frame)
		for task in _save_tasks:
			WorkerThreadPool.wait_for_task_completion(task)
		_save_tasks.clear()
	Game.quit_cleanly()


## One JPEG per drawn frame, numbered from the first frame after play(). The
## encode runs on the worker pool (the image is this frame's own copy), a few
## frames deep, so the GPU is not waiting on the JPEG writer.
func _save_frame() -> void:
	var image := get_viewport().get_texture().get_image()
	if image == null:
		return
	var path := "%s/%06d.jpg" % [_frames_dir, _frames_saved]
	_frames_saved += 1
	_save_tasks.append(WorkerThreadPool.add_task(func() -> void: image.save_jpg(path, 0.95), false, "frame jpeg"))
	while _save_tasks.size() > SAVE_TASKS_IN_FLIGHT:
		WorkerThreadPool.wait_for_task_completion(_save_tasks.pop_front())


func _next_shot() -> void:
	var previous_duration := TITLE_SECONDS if _index < 0 else _shots[_index].duration
	var remainder := maxf(_elapsed - previous_duration, 0.0)
	_index += 1
	_elapsed = remainder
	if _index >= _shots.size():
		if Game.has_flag("skip-cards"):
			_finish()
			return
		_phase = "end"
		# The world is fully covered during credits. Keep the UI drawing, but
		# avoid rendering the hidden supersampled forest for another 25 seconds.
		get_viewport().disable_3d = true
		Game.world.exposure_scale = 1.0
		_black.color.a = 1.0
		_clock.visible = false
		_caption_title.visible = false
		_caption.visible = false
		return
	_phase = "shot"
	_stone_cued = false
	_fire_cued = false
	_lanterns_cued = false
	_walk_step = 0
	_title.modulate.a = 0.0
	_subtitle.modulate.a = 0.0
	var shot := _shots[_index]
	print("CINEMATIC_SHOT index=%d name=%s time=%.3f" % [_index, shot.label, _total_elapsed])
	_path = Spline.new(_resolve(shot.path, shot.absolute))
	_look = Spline.new(_resolve(shot.look, shot.absolute))
	if not is_nan(shot.hour) and Game.world != null:
		Game.world.hour = shot.hour
	if not is_nan(shot.fire):
		Game.camp.campsite.firepit.intensity = shot.fire
	_shot_fire_start = Game.camp.campsite.firepit.intensity
	if shot.lantern_state >= 0:
		_set_lanterns(shot.lantern_state == 1)
	_update_shot()


func _update_shot() -> void:
	var shot := _shots[_index]
	if shot.feed_fire and not _fire_cued and _elapsed >= shot.feed_at:
		_fire_cued = true
		Game.camp.campsite.firepit.feed()
		if Game.audio != null:
			Game.audio.play_interact(&"log_added")
	if shot.skip_stone and not _stone_cued and _elapsed >= shot.stone_at:
		_stone_cued = true
		var dock: Dock = Game.camp.campsite.dock
		var origin := dock.to_global(Vector3(0.0, 0.9, -dock.total_length() - 0.2))
		Game.camp.pond.skip_stone(shot.stone_origin if shot.stone_origin.is_finite() else origin,
			shot.stone_direction if shot.stone_direction.length_squared() > 0.0 else -dock.global_basis.z)
	var t := clampf(_elapsed / shot.duration, 0.0, 1.0)
	var u := motion_progress(shot, _elapsed)
	var pos := camera_position(shot, _path, Game.camp.field, _elapsed)
	var target := aim_target(shot, pos, _look.sample(u), _elapsed)
	camera.global_position = pos
	if pos.distance_to(target) > 1e-3:
		camera.look_at(target, Vector3.UP)
	camera.fov = lerpf(shot.fov, shot.fov_end if not is_nan(shot.fov_end) else shot.fov, u)
	if shot.walk:
		var step := floori(_path.total_length() * u / WALK_STEP_LENGTH)
		if step > _walk_step and Game.audio != null:
			Game.audio.footstep(&"wood" if on_dock(pos) else shot.walk_surface, false, -6.0)
		_walk_step = step
	Game.world.weather.apply_chapter(shot.weather, _elapsed, shot.duration)
	if Game.camp.wildlife != null:
		Game.camp.wildlife.set_film_cue(shot.wildlife_cue, _elapsed)
	if not is_nan(shot.hour_end) and Game.world != null:
		Game.world.hour = lerpf(shot.hour, shot.hour_end, t)
	Game.world.exposure_scale = lerpf(shot.exposure, shot.exposure_end if not is_nan(shot.exposure_end) else shot.exposure, t)
	if not is_nan(shot.fire_end):
		Game.camp.campsite.firepit.intensity = lerpf(_shot_fire_start, shot.fire_end, smoothstep(0.0, 1.0, t))
	if not is_nan(shot.wind):
		Game.world.wind_scale = lerpf(shot.wind, shot.wind_end if not is_nan(shot.wind_end) else shot.wind, t)
	if not is_nan(shot.lantern_hour) and not _lanterns_cued and Game.world.hour >= shot.lantern_hour:
		_lanterns_cued = true
		_set_lanterns(true)
	_clock.visible = shot.clock
	if shot.clock:
		_clock.text = clock_text(Game.world.hour)
	var captioned := shot.caption != "" and not Game.has_flag("no-captions")
	_caption_title.visible = captioned
	_caption.visible = captioned
	if captioned:
		_caption_title.text = shot.label
		_caption.text = shot.caption
		var caption_alpha := smoothstep(shot.fade_in + 0.4, shot.fade_in + 1.4, _elapsed) \
			* (1.0 - smoothstep(shot.duration - shot.fade_out - 1.6, shot.duration - shot.fade_out - 0.5, _elapsed))
		_caption_title.modulate.a = caption_alpha
		_caption.modulate.a = caption_alpha
	var focus_distance := pos.distance_to(target)
	if not is_nan(shot.focus_distance):
		focus_distance = lerpf(shot.focus_distance, shot.focus_distance_end if not is_nan(shot.focus_distance_end) else shot.focus_distance, smoothstep(0.25, 0.75, t))
	apply_focus(shot.focus, focus_distance, not is_nan(shot.focus_distance))
	var fade := maxf(1.0 - _elapsed / shot.fade_in if shot.fade_in > 0 else 0.0,
		(_elapsed - (shot.duration - shot.fade_out)) / shot.fade_out if shot.fade_out > 0 else 0.0)
	_black.color.a = clampf(fade, 0.0, 1.0)
	if _elapsed >= shot.duration:
		_next_shot()


static func clock_text(hour: float) -> String:
	var minutes := floori(fposmod(hour, 24.0) * 60.0 + 0.00001) % 1440
	return "%02d:%02d" % [minutes / 60, minutes % 60]


const WALK_STEP_LENGTH := 0.64


## Re-evaluate the ground beneath each frame rather than interpolating only
## the control-point heights. Small, speed-weighted gait stops with the walker;
## its phase also drives the footfalls, so no bob or footsteps continue at rest.
static func camera_position(shot: Shot, path: Spline, field: TerrainField, seconds: float) -> Vector3:
	var u := motion_progress(shot, seconds)
	var pos := path.sample(u)
	if not shot.walk:
		return pos
	if not shot.absolute:
		pos.y = walking_support(field, pos) + shot.eye_height
	var before := motion_progress(shot, seconds - 1.0 / 120.0)
	var after := motion_progress(shot, seconds + 1.0 / 120.0)
	var speed := (after - before) * path.total_length() * 60.0
	var strength := smoothstep(0.02, 0.75, speed)
	var phase := u * path.total_length() / WALK_STEP_LENGTH * TAU
	var forward := path.sample(minf(u + 0.001, 1.0)) - path.sample(maxf(u - 0.001, 0.0))
	var side := forward.cross(Vector3.UP).normalized()
	return pos + Vector3.UP * (-cos(phase) * 0.012 * strength) + side * (sin(phase * 0.5) * 0.009 * strength)


static func on_dock(pos: Vector3) -> bool:
	var delta := Vector2(pos.x, pos.z) - TerrainField.DOCK_START
	var forward := (TerrainField.POND_CENTRE - TerrainField.DOCK_START).normalized()
	var along := delta.dot(forward)
	return along >= -Dock.INLAND and along <= Dock.LENGTH and absf(delta.cross(forward)) < Dock.WIDTH * 0.5


static func walking_support(field: TerrainField, pos: Vector3) -> float:
	var ground := field.height(pos.x, pos.z)
	var delta := Vector2(pos.x, pos.z) - TerrainField.DOCK_START
	var forward := (TerrainField.POND_CENTRE - TerrainField.DOCK_START).normalized()
	var along := delta.dot(forward)
	if along > Dock.LENGTH + 0.25:
		return ground
	# Anticipate the low threshold with a smooth step rather than snapping
	# eye height when a foot first crosses the inland edge of the boards.
	var board := TerrainField.WATER_LEVEL + Dock.DECK_ABOVE_WATER
	var edge := 1.0 - smoothstep(Dock.WIDTH * 0.5 - 0.25, Dock.WIDTH * 0.5 + 0.25, absf(delta.cross(forward)))
	edge *= 1.0 - smoothstep(Dock.LENGTH - 0.25, Dock.LENGTH + 0.25, along)
	return lerpf(ground, maxf(ground, board), edge * smoothstep(-Dock.INLAND - 0.35, -Dock.INLAND + 0.25, along))


static func aim_target(shot: Shot, pos: Vector3, base_target: Vector3, seconds: float) -> Vector3:
	var keys := shot.pitch_envelope
	if keys.size() < 2 or seconds < keys.front().x or seconds > keys.back().x:
		return base_target
	for i in range(1, keys.size()):
		if seconds > keys[i].x:
			continue
		var a := keys[i - 1]
		var b := keys[i]
		var t := _travel_progress(seconds - a.x, b.x - a.x, shot.pitch_ramp_seconds)
		var ray := base_target - pos
		var flat := Vector3(ray.x, 0.0, ray.z)
		var pitch := lerpf(atan2(ray.y, flat.length()), deg_to_rad(lerpf(a.y, b.y, t)), lerpf(a.z, b.z, t))
		return pos + (flat.normalized() * cos(pitch) + Vector3.UP * sin(pitch)) * ray.length()
	return base_target


## Held compositions flank a slow move. A cosine velocity ramp reaches a
## constant cruise without the mid-shot speed surge of whole-shot easing.
static func motion_progress(shot: Shot, seconds: float) -> float:
	if shot.ramp_seconds <= 0.0:
		var t := clampf(seconds / shot.duration, 0.0, 1.0)
		return lerpf(t, smoothstep(0.0, 1.0, t), 0.75)
	var travel := maxf(shot.duration - shot.hold_start - shot.hold_end, 0.01)
	var t := clampf(seconds - shot.hold_start, 0.0, travel)
	if shot.rest_seconds > 0.0:
		var moving := maxf(travel - shot.rest_seconds, 0.01)
		var arrival := moving * shot.rest_at
		if t < arrival:
			return shot.rest_at * _travel_progress(t, arrival, shot.ramp_seconds)
		if t < arrival + shot.rest_seconds:
			return shot.rest_at
		return shot.rest_at + (1.0 - shot.rest_at) * _travel_progress(
			t - arrival - shot.rest_seconds, moving - arrival, shot.ramp_seconds)
	return _travel_progress(t, travel, shot.ramp_seconds)


static func _travel_progress(seconds: float, travel: float, ramp_seconds: float) -> float:
	var t := clampf(seconds, 0.0, travel)
	var ramp := minf(ramp_seconds, travel * 0.5)
	var distance := travel - ramp
	if t < ramp:
		return 0.5 * (t - ramp / PI * sin(PI * t / ramp)) / distance
	if t > travel - ramp:
		var remaining := travel - t
		return 1.0 - 0.5 * (remaining - ramp / PI * sin(PI * remaining / ramp)) / distance
	return (t - ramp * 0.5) / distance


static func _set_lanterns(lit: bool) -> void:
	var site: Campsite = Game.camp.campsite
	for lantern in site.lanterns:
		lantern.set_lit(lit)
	site.tent.lantern.set_lit(lit)
	site.dock.lantern.set_lit(lit)


static func apply_focus(focus: bool, distance: float, tight := false) -> void:
	if Game.world == null:
		return
	var a: CameraAttributesPractical = Game.world.attributes
	a.dof_blur_far_enabled = focus
	a.dof_blur_near_enabled = focus
	if focus:
		a.dof_blur_far_distance = distance + 0.15 if tight else distance * 1.6
		a.dof_blur_far_transition = 0.65 if tight else distance * 3.0
		a.dof_blur_near_distance = maxf(0.01, distance - 0.15) if tight else distance * 0.45
		a.dof_blur_near_transition = 0.65 if tight else distance * 0.4
	a.dof_blur_amount = 0.10 if tight else 0.045


## Ground-relative points (y above the ground or water under them) become
## absolute world positions; absolute shots pass through untouched.
static func _resolve(points: Array[Vector3], absolute: bool) -> Array[Vector3]:
	return resolve(Game.camp.field, points, absolute)


## Relative shot points are metres above the ground (or the water, whichever is
## higher); absolute ones are world space.
static func resolve(field: TerrainField, points: Array[Vector3], absolute: bool) -> Array[Vector3]:
	if absolute:
		return points.duplicate()
	var out: Array[Vector3] = []
	for p in points:
		var ground := maxf(field.height(p.x, p.z), TerrainField.WATER_LEVEL)
		out.append(Vector3(p.x, ground + p.y, p.z))
	return out


## Samples every shot of `sequence_name` and lists the points where the lens
## would sit inside the ground or the pond bed, a tree trunk or crown, a dock
## pile, the moored canoe, the tent or the fire tripod. Empty when the shots
## are clear; the camp test keeps it that way.
static func obstructions(field: TerrainField, plan: ScenePlan, sequence_name: String,
		piles: Array[Vector3], canoe_position: Vector3, canoe_yaw: float, samples := 48) -> PackedStringArray:
	var out := PackedStringArray()
	var canoe_axis := Vector2(sin(canoe_yaw), cos(canoe_yaw)) * Canoe.LENGTH * 0.5
	var canoe_xz := Vector2(canoe_position.x, canoe_position.z)
	var shots := sequence(sequence_name)
	for si in shots.size():
		var shot := shots[si]
		var path := Spline.new(resolve(field, shot.path, shot.absolute))
		for i in samples:
			var p := camera_position(shot, path, field, shot.duration * float(i) / float(samples - 1))
			var xz := Vector2(p.x, p.z)
			var ground := field.height(p.x, p.z)
			if p.y < ground + 0.25:
				out.append("shot %d: ground %.2f under lens at %s" % [si, ground, p])
			for pile in piles:
				if p.y < pile.y + 0.3 and xz.distance_to(Vector2(pile.x, pile.z)) < 0.45:
					out.append("shot %d: pile %s at lens %s" % [si, pile, p])
			if p.y > TerrainField.WATER_LEVEL - 0.7 and p.y < TerrainField.WATER_LEVEL + 0.8:
				var d := _segment_distance(xz, canoe_xz - canoe_axis, canoe_xz + canoe_axis)
				if d < Canoe.HALF_BEAM + 0.35:
					out.append("shot %d: canoe %.2f m from lens at %s" % [si, d, p])
			if xz.distance_to(TerrainField.TENT) < 2.0 and p.y < ground + Tent.HEIGHT + 0.35:
				var local := tent_transform(field).affine_inverse() * p
				var roof_clearance := Tent.WIDTH * 0.5 * (1.0 - local.y / Tent.HEIGHT) - absf(local.x)
				if not shot.interior or roof_clearance < 0.18 or absf(local.z) > Tent.LENGTH * 0.5 - 0.25:
					out.append("shot %d: tent at lens %s" % [si, p])
			if xz.distance_to(TerrainField.FIRE) < 0.8 and p.y < ground + 1.5:
				out.append("shot %d: fire tripod at lens %s" % [si, p])
			if xz.distance_to(TerrainField.FIRE) < 2.0:
				var local := p - Vector3(TerrainField.FIRE.x, field.height(0, 0), TerrainField.FIRE.y)
				for leg_index in 3:
					var leg := Firepit.tripod_leg(leg_index)
					var axis := leg[1] - leg[0]
					var along := clampf((local - leg[0]).dot(axis) / axis.length_squared(), 0.0, 1.0)
					if local.distance_to(leg[0] + axis * along) < 0.2:
						out.append("shot %d: tripod leg at lens %s" % [si, p])
			if xz.distance_to(TerrainField.TABLE) < 1.15 and p.y < ground + 1.55:
				out.append("shot %d: cooking table at lens %s" % [si, p])
			for seat in TerrainField.SEATS:
				var outward := (seat - TerrainField.FIRE).normalized()
				var tangent := Vector2(outward.y, -outward.x)
				var offset := xz - seat
				if absf(offset.dot(tangent)) < 1.08 and absf(offset.dot(outward)) < 0.36 and p.y < ground + 0.80:
					out.append("shot %d: bench at lens %s" % [si, p])
			for t in plan.near_trees():
				var species := TreeSpecies.by_kind(t.kind)
				var base := field.height(t.position.x, t.position.y)
				var top := base + species.height.y * t.scale + 1.0
				var crown_base := base + species.branch_start * species.height.x * t.scale - 0.6
				var radius := ScenePlan.crown_footprint(t.kind) * t.scale + 0.5
				var dxz := xz.distance_to(t.position)
				if dxz < 1.0 and p.y < top:
					out.append("shot %d: trunk %s %s at lens %s" % [si, species.name, t.position, p])
				elif dxz < radius and p.y > crown_base and p.y < top:
					out.append("shot %d: crown %s %s at lens %s" % [si, species.name, t.position, p])
	return out


static func _segment_distance(p: Vector2, a: Vector2, b: Vector2) -> float:
	var ab := b - a
	var t := clampf((p - a).dot(ab) / maxf(ab.length_squared(), 1e-6), 0.0, 1.0)
	return p.distance_to(a + ab * t)


# ------------------------------------------------------------------ authoring

static func _shot(path: Array[Vector3], look: Array[Vector3], duration: float, fov := 50.0, absolute := false) -> Shot:
	var s := Shot.new()
	s.path = path
	s.look = look
	s.duration = duration
	s.fov = fov
	s.absolute = absolute
	return s


## Camera positions on an arc around `centre` (ground-relative heights).
static func _orbit(centre: Vector3, radius: float, height: float, from_deg: float, to_deg: float, steps := 9) -> Array[Vector3]:
	var out: Array[Vector3] = []
	for i in steps:
		var a := deg_to_rad(lerpf(from_deg, to_deg, float(i) / float(steps - 1)))
		out.append(Vector3(centre.x + cos(a) * radius, height, centre.z + sin(a) * radius))
	return out


static func _repeat(p: Vector3, count := 2) -> Array[Vector3]:
	var out: Array[Vector3] = []
	for i in count:
		out.append(p)
	return out


static func arrival() -> Array[Shot]:
	var shots: Array[Shot] = []
	var dolly := _shot(IntroDolly.PATH, IntroDolly.LOOK, 17.0, 38.0, true)
	dolly.fov_end = 64.0
	dolly.fade_in = 1.2
	shots.append(dolly)
	var fire_target := Vector3(0.4, 0.75, -0.9)
	var orbit := _shot(_orbit(Vector3(0.3, 0.0, -0.8), 5.6, 1.7, 215.0, 335.0), _repeat(fire_target), 12.0, 46.0)
	shots.append(orbit)
	var close := _shot([Vector3(1.9, 0.5, 1.7), Vector3(1.05, 0.55, 0.95)], [Vector3(0.0, 0.35, 0.0), Vector3(0.0, 0.4, 0.0)], 8.0, 34.0)
	close.focus = true
	shots.append(close)
	var pile := _shot([Vector3(3.0, 1.3, 1.2), Vector3(4.4, 1.25, 0.0)], [Vector3(5.4, 0.55, -2.4), Vector3(6.9, 0.9, -4.0)], 7.0, 40.0)
	pile.focus = true
	shots.append(pile)
	return shots


static func pond() -> Array[Shot]:
	var shots: Array[Shot] = []
	shots.append(_shot([Vector3(-5.0, 1.65, -1.5), Vector3(-11.0, 1.6, -0.2), Vector3(-14.0, 1.55, 3.5)],
			[Vector3(-20.0, 0.8, 4.0), Vector3(-24.0, 0.6, 5.5), Vector3(-30.0, 0.4, 5.0)], 10.0, 52.0))
	shots.append(_shot([Vector3(-16.4, 1.1, 6.2), Vector3(-23.0, 1.1, 5.4)],
			[Vector3(-28.0, -0.4, 4.0), Vector3(-42.0, 1.2, 0.0)], 9.0, 50.0, true))
	var canoe := _shot([Vector3(-24.5, 0.0, 1.0), Vector3(-22.2, 0.05, 0.6)],
			[Vector3(-21.0, -0.6, 3.8), Vector3(-20.6, -0.5, 4.0)], 6.0, 40.0, true)
	canoe.focus = true
	shots.append(canoe)
	shots.append(_shot([Vector3(-22.0, -0.25, 9.5), Vector3(-33.0, -0.35, 6.0), Vector3(-43.0, -0.2, 1.0)],
			[Vector3(-36.0, -0.6, 4.0), Vector3(-50.0, -0.5, -4.0), Vector3(-55.0, 1.5, -8.0)], 9.0, 56.0, true))
	# Under the surface past the end piles (a pile stays in frame, a metre
	# off the lens), then a breach in open water looking back at the jetty.
	shots.append(_shot([Vector3(-21.8, -1.8, 2.9), Vector3(-26.5, -2.3, 4.0)],
			[Vector3(-30.0, -2.5, 5.0), Vector3(-35.0, -3.0, 4.0)], 7.0, 60.0, true))
	shots.append(_shot([Vector3(-30.0, -1.6, 2.0), Vector3(-29.0, 0.7, 1.2)],
			[Vector3(-22.0, 0.0, 5.0), Vector3(-16.0, 1.2, 6.0)], 6.0, 55.0, true))
	return shots


static func nightfall() -> Array[Shot]:
	var shots: Array[Shot] = []
	var lapse := _shot([Vector3(7.0, 1.55, 7.0), Vector3(6.2, 1.6, 7.6)], _repeat(Vector3(0.5, 0.9, -1.0)), 14.0, 48.0)
	lapse.hour = 19.35
	lapse.hour_end = 23.6
	shots.append(lapse)
	var orbit := _shot(_orbit(Vector3(0.0, 0.0, 0.0), 4.2, 1.25, 10.0, 150.0), _repeat(Vector3(0.0, 0.45, 0.0)), 10.0, 42.0)
	orbit.hour = 23.4
	orbit.feed_fire = true
	shots.append(orbit)
	var tent := _shot([Vector3(2.8, 1.4, 1.5), Vector3(4.8, 1.3, -1.2)], _repeat(Vector3(7.5, 0.9, -4.5)), 7.0, 44.0)
	tent.hour = 23.4
	shots.append(tent)
	var dock := _shot([Vector3(-14.0, 1.6, 9.0), Vector3(-18.5, 1.5, 8.0)], [Vector3(-24.5, 0.5, 5.0), Vector3(-30.0, 2.0, 0.0)], 8.0, 50.0)
	dock.hour = 23.6
	shots.append(dock)
	return shots


## A compact film progressing from open water to the last light at camp.
## Each composition has one subject and enough lateral motion for parallax.
static func afterglow() -> Array[Shot]:
	var shots: Array[Shot] = []
	var lake := _shot([Vector3(-15.5, 1.8, 9.0), Vector3(-14.7, 1.9, 10.3)],
		[Vector3(-39.0, 1.0, -1.0), Vector3(-37.0, 1.2, -2.0)], 7.0, 54.0)
	lake.label = "The still water"
	lake.hour = 19.05
	lake.fade_in = 1.2
	shots.append(lake)
	var lilies := _shot([Vector3(-27.0, -0.15, 11.2), Vector3(-28.0, -0.23, 11.8)],
		_repeat(Vector3(-29.2, -0.87, 14.4)), 6.0, 40.0, true)
	lilies.label = "Life at the edge"
	lilies.caption = "Gerstner waves with lily pads and stems riding the surface; pond simulation with persistent ripples"
	lilies.hour = 19.15
	lilies.focus = true
	shots.append(lilies)
	var meadow := _shot([Vector3(9.0, 0.9, 8.0), Vector3(7.5, 1.05, 8.4)],
		_repeat(Vector3(-0.5, 1.0, -0.5)), 6.0, 48.0)
	meadow.label = "Through the meadow"
	meadow.caption = "Geometry grass and wildflowers in one wind field; photoscanned ferns and shrubs; wildlife on cue"
	meadow.hour = 19.20
	shots.append(meadow)
	var kitchen := _shot([Vector3(7.6, 1.45, 3.0), Vector3(8.1, 1.42, 3.1)],
		_repeat(Vector3(9.1, 0.93, 1.1)), 6.0, 43.0)
	kitchen.label = "Room for two"
	kitchen.caption = "Jolt soft-body tablecloth; generated props beside Poly Haven photoscans; bokeh depth of field"
	kitchen.hour = 19.35
	kitchen.focus = true
	shots.append(kitchen)
	var fire := _shot([Vector3(0.1, 0.85, 2.5), Vector3(0.45, 0.90, 2.35)],
		_repeat(Vector3(0.0, 0.68, 0.0)), 6.0, 45.0)
	fire.label = "The hearth"
	fire.hour = 20.20
	fire.focus = true
	shots.append(fire)
	var ripples := _shot([Vector3(-23.5, 0.7, 8.0), Vector3(-24.2, 0.65, 8.2)],
		_repeat(Vector3(-28.5, -0.75, 3.8)), 7.0, 50.0, true)
	ripples.label = "One last stone"
	ripples.hour = 20.35
	ripples.skip_stone = true
	shots.append(ripples)
	var camp := _shot([Vector3(6.3, 1.5, 7.2), Vector3(7.0, 1.7, 8.2)],
		_repeat(Vector3(2.8, 1.0, -1.8)), 7.0, 48.0)
	camp.label = "Stay a little longer"
	camp.hour = 21.20
	camp.fade_out = 0.7
	shots.append(camp)
	var stars := _shot([Vector3(-15.5, 2.1, 9.0), Vector3(-15.5, 2.9, 9.5)],
		[Vector3(-65.0, 19.0, 8.0), Vector3(-65.0, 28.0, 8.0)], 8.0, 60.0)
	stars.label = "Under the stars"
	stars.caption = "Star field and Milky Way; star reflections on still water as the storm clears"
	stars.hour = 23.4
	stars.fade_in = 1.2
	stars.fade_out = 1.8
	shots.append(stars)
	return shots


static func sequence(name: String) -> Array[Shot]:
	match name:
		"showcase":
			return showcase()
		"storm":
			var weather_shots: Array[Shot] = []
			for shot in one_night():
				if shot.weather in ["storm", "storm_short", "rain_detail", "shelter_rain"]:
					weather_shots.append(shot)
			return weather_shots
		"one_night":
			return one_night()
		"afterglow":
			return afterglow()
		"arrival":
			return arrival()
		"pond":
			return pond()
		"nightfall":
			return nightfall()
		"showreel":
			var all: Array[Shot] = []
			all.append_array(arrival())
			all.append_array(pond())
			all.append_array(nightfall())
			return all
		_:
			return []


## Closing card plus the credit roll: long enough to read every creator in
## the five-minute film, brisker after the short reel.
static func end_seconds(name: String) -> float:
	match name:
		"one_night":
			return 62.0
		"showcase":
			return 38.0
	return END_SECONDS


static func duration(name: String) -> float:
	var seconds := TITLE_SECONDS + end_seconds(name)
	for shot in sequence(name):
		seconds += shot.duration
	return seconds


static func score_cues(_name: String) -> Array[Dictionary]:
	# Retain the standalone synthesis study, but every film is nature-only.
	return []


static func tent_transform(field: TerrainField) -> Transform3D:
	var at := TerrainField.TENT
	return Transform3D(Basis.looking_at(Vector3(-at.x, 0, -at.y).normalized(), Vector3.UP),
		Vector3(at.x, field.height(at.x, at.y), at.y))


## Five-minute camper's journey: each pause has a subject or a visible event.
## Five seconds of titles + 270 seconds of scenes + 62 seconds of card and credits.
static func one_night() -> Array[Shot]:
	var shots: Array[Shot] = []
	var field := TerrainField.new()
	var arrival := _shot([Vector3(2, 1.64, 34), Vector3(1.5, 1.64, 20), Vector3(1, 1.64, 9)],
		[Vector3(1.5, 0.8, 23), Vector3(0.5, 0.9, 0), Vector3(1.0, 1.0, -1.2)], 25.0, 58.0)
	arrival.label = "A clearing in the woods"
	arrival.caption = "Procedural woodland: 1,278 generated trees around the camp and 21,000 on the hills; SDFGI and volumetric fog"
	arrival.fov_end = 54.0
	arrival.walk = true
	arrival.lantern_state = 0
	arrival.fire = 1.0
	shots.append(arrival)
	var meadow := _shot([Vector3(6.8, 0.72, 9.4), Vector3(6.35, 0.76, 9.55)],
		[Vector3(4.3, 0.78, 10.2), Vector3(4.0, 0.70, 10.5)], 12.0, 44.0)
	meadow.label = "A breeze through the meadow"
	meadow.focus = true
	meadow.wildlife_cue = &"meadow_life"
	shots.append(meadow)
	var kitchen := _shot([Vector3(7.5, 1.22, 3.0), Vector3(7.75, 1.10, 2.85)],
		_repeat(Vector3(9.18, 1.08, 1.16)), 10.0, 43.0)
	kitchen.label = "A place for two"
	kitchen.focus = true
	kitchen.focus_distance = 1.85
	kitchen.focus_distance_end = 2.7
	kitchen.exposure = 0.95
	shots.append(kitchen)
	var approach := _shot([Vector3(-3, 1.64, 0.8), Vector3(-6, 1.64, -2.5), Vector3(-12, 1.64, -0.5),
		Vector3(-14.5, 1.64, 3), Vector3(-15.9, 1.64, 6.3), Vector3(-18.5, 1.64, 5.97)],
		[Vector3(-19, 0.6, 2), Vector3(-25, 0.3, 4.0), Vector3(-30, 0.2, 4)], 23.0, 56.0)
	approach.label = "Down to the water"
	approach.caption = "Parallax-mapped terrain, shoreline wetness; a grounded first-person walk with surface-aware footsteps"
	approach.walk = true
	approach.exposure = 0.90
	shots.append(approach)
	var birds := _shot([Vector3(-16.5, 1.0, 7.7), Vector3(-16.7, 1.02, 7.75)],
		[Vector3(-19.723, 0.21, 6.612), Vector3(-25.5, 0.8, 4.2)], 13.0, 43.0, true)
	birds.label = "A visitor on the jetty"
	birds.caption = "Planar reflections from a mirrored camera; procedural birds; sky, fog and sun from one atmosphere model"
	birds.wildlife_cue = &"pond_birds"
	birds.exposure = 0.95
	shots.append(birds)
	var lilies := afterglow()[1]
	lilies.label = "Life at the waterline"
	lilies.duration = 12.0
	lilies.wildlife_cue = &"pond_life"
	shots.append(lilies)
	# Keep the complete, reviewed entry/immersion/breach path at its original
	# 28-second pace. The preceding stone throw ends at precisely this pose.
	var water_start := Vector3(-20.4, -0.35, 7.8)
	var water_look := Vector3(-30.5, -0.4, 9.2)
	var stone := _shot([Vector3(-20.2, -0.30, 8.0), water_start],
		[Vector3(-30.3, -0.35, 9.4), water_look], 10.0, 64.0, true)
	stone.label = "Three skips"
	stone.caption = "Physics-driven skipping stone; ripples, foam and sun glitter on the water"
	stone.skip_stone = true
	stone.stone_at = 3.0
	stone.stone_origin = Vector3(-21.2, -0.1, 7.9)
	stone.stone_direction = Vector3(-1.0, 0.0, 0.15)
	stone.exposure = 0.9
	shots.append(stone)
	var water := _shot([water_start, Vector3(-21.0, -1.15, 7.60), Vector3(-21.9, -1.90, 7.00),
		Vector3(-22.3, -2.00, 6.85), Vector3(-22.4, -1.30, 6.90), Vector3(-22.45, -1.02, 6.95),
		Vector3(-22.55, -0.858, 7.05), Vector3(-22.70, -0.858, 7.55), Vector3(-22.9, -0.858, 8.1),
		Vector3(-22.1, -0.65, 7.95), Vector3(-21.9, -0.35, 7.85)],
		[water_look, Vector3(-30, -1.3, 8.4), Vector3(-30, -1.3, 8.4),
		Vector3(-31.5, 0.2, 6.3), Vector3(-32, 0.5, 3.2)], 28.0, 64.0, true)
	water.label = "Beneath the reflections"
	water.caption = "Underwater: caustics, sun shafts, silt and Snell's window through the moving surface"
	water.fov_end = 56.0
	water.continuous_in = true
	water.exposure = 0.9
	water.pitch_envelope = [Vector3(11.8, -11.0, 0.0), Vector3(15.1, -11.0, 1.0),
		Vector3(17.8, -11.0, 1.0), Vector3(21.5, -11.0, 0.0)]
	shots.append(water)
	var sunset := _shot([Vector3(-15.5, 1.65, 9), Vector3(-15.15, 1.66, 9.25)],
		[Vector3(-39, 1.2, -1), Vector3(-37, 2.0, -1.8)], 22.0, 54.0)
	sunset.label = "The last light"
	sunset.caption = "Single-scattering sky and sun glitter; the same world through a full day and night cycle"
	sunset.lantern_hour = 20.30
	sunset.exposure = 0.85
	sunset.exposure_end = 1.0
	shots.append(sunset)
	var returning := _shot([Vector3(-8, 1.64, 5.0), Vector3(-5, 1.64, 4.1),
		Vector3(-3, 1.64, 3.3), Vector3(-1.6, 1.64, 2.6)],
		[Vector3(0, 0.72, 0), Vector3(0, 0.72, 0)], 12.0, 52.0)
	returning.label = "Back to the fire"
	returning.caption = "Moonlight, fireflies and a warming fire; exposure follows the fading light"
	returning.walk = true
	returning.walk_surface = &"grass"
	returning.exposure = 1.0
	returning.fire = 0.55
	shots.append(returning)
	var hearth := _shot([Vector3(-1.6, 1.64, 2.6), Vector3(-1.1, 1.15, 2.3), Vector3(-0.7, 1.0, 2.1)],
		[Vector3(0, 0.72, 0), Vector3(0, 0.55, 0)], 14.0, 52.0)
	hearth.label = "Tending the hearth"
	hearth.caption = "Ray-marched volumetric flames; GPU sparks and lit smoke that wraps the pot; interactive fire"
	hearth.continuous_in = true
	hearth.feed_fire = true
	hearth.feed_at = 6.0
	shots.append(hearth)
	var tent_out := _shot([Vector3(4.1, 1.30, 0.25), Vector3(4.6, 1.30, -0.10)],
		[Vector3(7.35, 1.05, -4.4), Vector3(7.65, 1.05, -4.5)], 10.0, 37.0)
	tent_out.label = "The shelter glows"
	tent_out.caption = "Canvas translucency and lantern light; SDFGI bounce across the camp"
	tent_out.weather = "gathering"
	shots.append(tent_out)
	var storm := _shot([Vector3(-16.8, 1.38, 9.0), Vector3(-17.1, 1.4, 9.15)],
		[Vector3(-34, 2.0, 0.4), Vector3(-34, 5.0, -0.2)], 20.0, 61.0)
	storm.label = "A storm across the pond"
	storm.caption = "Ray-marched cumulus; lightning lights the clouds in the same frame; world-space rain"
	storm.weather = "storm_short"
	shots.append(storm)
	var wet_deck := _shot([Vector3(-20.65, -0.02, 6.07), Vector3(-21.05, 0.08, 6.01)],
		[Vector3(-23.5, -0.28, 5.20), Vector3(-23.8, -0.35, 4.9)], 10.0, 48.0, true)
	wet_deck.label = "Rain on the timber"
	wet_deck.caption = "Wet materials; rain impacts and lantern reflections on the timber"
	wet_deck.weather = "rain_detail"
	shots.append(wet_deck)
	var tent := tent_transform(field)
	var interior := _shot([tent * Vector3(0.45, 0.70, -0.15), tent * Vector3(0.43, 0.72, -0.23)],
		_repeat(Vector3(0, field.height(0, 0) + 0.9, 0)), 14.0, 64.0, true)
	interior.label = "Rain on the canvas"
	interior.caption = "Sheltered rain; translucent canvas and soft-body cloth; recorded thunder"
	interior.weather = "shelter_rain"
	interior.interior = true
	interior.exposure = 0.9
	shots.append(interior)
	var star_pos := Vector3(-23.7, -0.30, 9.6)
	var star_look: Array[Vector3] = []
	var reflection_dir := (Vector3(-24.25, -1.1, 5.52) - star_pos).normalized()
	var sky_dir := (Vector3(-58, 28, -4) - star_pos).normalized()
	for i in 7:
		star_look.append(star_pos + reflection_dir.slerp(sky_dir, float(i) / 6.0) * 50.0)
	var stars := _shot(_repeat(star_pos), star_look, 18.0, 60.0, true)
	stars.label = "While the world sleeps"
	stars.weather = "clearing"
	stars.fire_end = 0.12
	shots.append(stars)
	var dawn := _shot([Vector3(1.5, 1.64, 20), Vector3(1.0, 1.64, 11)],
		[Vector3(1.0, 1.0, -1.2), Vector3(1.0, 1.0, -1.2)], 17.0, 54.0)
	dawn.label = "First light"
	dawn.caption = "Dawn fog and aerial perspective; the clearing again at first light"
	dawn.walk = true
	dawn.weather = "dawn"
	dawn.fire_end = 0.08
	dawn.exposure = 0.90
	dawn.exposure_end = 1.0
	shots.append(dawn)
	var hours := [17.00, 17.18, 17.23, 17.31, 17.44, 17.50, 17.60, 17.70, 17.85,
		20.95, 21.07, 21.40, 21.55, 21.90, 21.95, 22.15, 23.15, 30.60]
	var winds := [0.75, 0.90, 1.05, 0.85, 0.70, 0.70, 0.65, 0.70, 0.70,
		0.45, 0.55, 0.85, 1.30, 1.55, 1.55, 0.85, 0.25, 0.60]
	for i in shots.size():
		var shot := shots[i]
		shot.hour = hours[i]
		shot.hour_end = hours[i + 1]
		shot.wind = winds[i]
		shot.wind_end = winds[i + 1]
		shot.clock = true
		shot.fade_in = 0.8
		shot.fade_out = 0.8
		shot.hold_start = 0.8
		shot.hold_end = 1.2
		shot.ramp_seconds = 1.5
	arrival.fade_in = 1.25
	arrival.hold_start = 0.7
	arrival.hold_end = 1.5
	approach.hold_start = 0.6
	approach.hold_end = 0.8
	birds.hold_start = 3.0
	birds.hold_end = 1.0
	returning.fade_out = 0.0
	hearth.fade_in = 0.0
	hearth.hold_end = 3.0
	stone.fade_out = 0.0
	stone.hold_end = 0.6
	water.fade_in = 0.0
	water.hold_start = 0.6
	water.hold_end = 6.2
	water.ramp_seconds = 2.5
	water.rest_at = Spline.new(water.path).progress_at_point(3)
	water.rest_seconds = 3.5
	stars.hold_start = 1.0
	stars.hold_end = 1.5
	# An overnight fade is preferable to racing five hours of star motion.
	# Open in civil twilight so the closing walk actually carries first light:
	# the sky is already warming and the sun clears the trees before the end.
	dawn.hour = 29.55
	dawn.fade_in = 1.4
	dawn.fade_out = 1.6
	return shots


## Compact environmental study: real movement, a close subject, water,
## weather and the warm camp. Same world, native sound, no enlarged wildlife.
static func showcase() -> Array[Shot]:
	var shots: Array[Shot] = []
	# Walk in toward the camp, as the film does, instead of turning to the pond.
	var arrival := _shot([Vector3(1.6, 1.64, 24), Vector3(1.2, 1.64, 16), Vector3(1.0, 1.64, 10)],
		[Vector3(1.0, 0.9, -1.2), Vector3(1.0, 1.0, -1.2)], 10.0, 56.0)
	arrival.label = "A clearing in the woods"
	arrival.caption = "Procedural woodland: 1,278 generated trees around the camp and 21,000 on the hills; SDFGI and volumetric fog"
	arrival.walk = true
	arrival.hour = 18.1
	arrival.lantern_state = 1
	arrival.fire = 1.0
	shots.append(arrival)
	var close := one_night()[2]
	close.label = "A place to stay"
	close.caption = "Jolt soft-body tablecloth; generated props beside Poly Haven photoscans; bokeh depth of field"
	close.duration = 6.0
	close.hour = 18.2
	close.hour_end = NAN
	close.hold_start = 0.5
	close.hold_end = 0.5
	close.focus_distance = 1.85
	close.focus_distance_end = 2.4
	shots.append(close)
	var tent := _shot([Vector3(4.4, 1.35, 0.4), Vector3(4.8, 1.3, -0.2)],
		[Vector3(7.35, 1.05, -4.4), Vector3(7.65, 1.05, -4.5)], 6.0, 40.0)
	tent.label = "The shelter"
	tent.caption = "Sewn canvas with translucency; lantern, woodpile and photoscanned camp props"
	tent.hour = 18.3
	shots.append(tent)
	var down := _shot([Vector3(-12, 1.64, -0.5), Vector3(-14.5, 1.64, 3), Vector3(-15.9, 1.64, 6.3), Vector3(-18.5, 1.64, 5.97)],
		[Vector3(-19, 0.6, 2), Vector3(-25, 0.3, 4.0), Vector3(-30, 0.2, 4)], 11.0, 56.0)
	down.label = "Down to the water"
	down.caption = "Parallax-mapped terrain, shoreline wetness; a grounded first-person walk with surface-aware footsteps"
	down.walk = true
	down.hour = 18.5
	down.exposure = 0.92
	shots.append(down)
	var pond_view := _shot([Vector3(-15.1, 1.65, 9.5), Vector3(-15.7, 1.65, 9.15)],
		[Vector3(-37.5, 0.2, -1), Vector3(-40, 0.2, -2)], 7.0, 57.0)
	pond_view.label = "A living shoreline"
	pond_view.caption = "Planar reflections from a mirrored camera; Gerstner waves; sky, fog and sun from one atmosphere model"
	pond_view.hour = 18.8
	shots.append(pond_view)
	var life := one_night()[4]
	life.duration = 7.0
	life.hour = 18.0
	life.hour_end = NAN
	life.label = "The visitor"
	life.caption = "Procedural birds and fish on cue; lily pads riding the waves"
	shots.append(life)
	var rain := one_night()[13]
	rain.duration = 6.0
	rain.hour = 19.2
	rain.hour_end = NAN
	rain.label = "Rain on timber"
	rain.caption = "Wet materials; rain impacts and lantern reflections on the timber"
	shots.append(rain)
	# Low in the wet grass behind scanned ferns, looking across the clearing to
	# the tent; the hero oak stands at the frame's right edge.
	var aftermath := _shot([Vector3(21.2, 1.3, -10.6), Vector3(20.5, 1.32, -10.1)],
		[Vector3(12.0, 1.9, -5.8), Vector3(11.8, 2.0, -5.5)], 7.0, 50.0)
	aftermath.hour = 17.5
	aftermath.weather = "dawn"
	aftermath.label = "After the rain"
	aftermath.caption = "Scene-wide wetness, low mist and sun shafts through the damp air"
	shots.append(aftermath)
	var hearth := _shot([Vector3(4.6, 1.55, 3.8), Vector3(4.1, 1.5, 4.1)],
		[Vector3(1.0, 0.70, -0.5), Vector3(2.0, 0.85, -1.6)], 8.0, 54.0)
	hearth.hour = 21.20
	hearth.label = "Stay a little longer"
	hearth.caption = "Ray-marched volumetric flames; GPU sparks and lit smoke; fireflies and moonlight"
	hearth.feed_fire = true
	hearth.feed_at = 2.0
	shots.append(hearth)
	for shot in shots:
		shot.clock = true
		shot.fade_in = 0.45
		shot.fade_out = 0.45
		shot.ramp_seconds = 1.1
		shot.exposure = 1.0 if is_nan(shot.exposure) or shot.exposure == 1.0 else shot.exposure
		shot.exposure_end = NAN
	return shots
