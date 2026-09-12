extends Node

## Global session state and cross-system references.
##
## Systems register themselves here when they enter the tree so that loosely
## coupled parts (HUD, player, world, audio) can find each other without deep
## node paths. Nothing here does rendering or simulation work itself.

signal mode_changed(mode: Mode)
signal hud_visibility_changed(visible: bool)

enum Mode { LOADING, INTRO, PLAY, PHOTO, PAUSED }

const SCREENSHOT_DIR := "user://screenshots"

var mode: Mode = Mode.LOADING:
	set(value):
		if value == mode:
			return
		mode = value
		_apply_mouse_mode()
		mode_changed.emit(mode)

var hud_visible := true:
	set(value):
		hud_visible = value
		hud_visibility_changed.emit(value)

var player: Node3D
var world: Node3D
var camp: Node3D
var audio: Node
var hud: CanvasLayer
var photo_camera: Camera3D

## Parsed `--key=value` / `--flag` arguments passed after `--` on the command line.
var user_args: Dictionary = {}
var _quitting := false


## Imported Ogg/WAV playbacks release their decoder data on the audio mixer
## thread. Let it finish after stopping players, before shutting down Godot.
func quit_cleanly(code := 0) -> void:
	if _quitting:
		return
	_quitting = true
	get_tree().paused = true
	_stop_audio(get_tree().root)
	await get_tree().create_timer(0.25, true, false, true).timeout
	get_tree().quit(code)


func _stop_audio(node: Node) -> void:
	if node is AudioStreamPlayer or node is AudioStreamPlayer3D or node is AudioStreamPlayer2D:
		node.stop()
	for child in node.get_children():
		_stop_audio(child)


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	user_args = parse_user_args(OS.get_cmdline_user_args())
	_apply_movie_size()
	_apply_mouse_mode()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_fullscreen"):
		toggle_fullscreen()
	elif event.is_action_pressed("screenshot"):
		save_screenshot()
	elif event.is_action_pressed("toggle_hud"):
		hud_visible = not hud_visible


## `--movie-size=WxH` renders the root viewport at that size and scales it to
## the window, so Movie Maker mode writes frames at that resolution whatever
## the screen is: 4K captures from a 1080p display. Applied before the first
## frame because the movie writer fixes its frame size on frame one.
func _apply_movie_size() -> void:
	var spec := arg_value("movie-size", "")
	if not spec.contains("x"):
		return
	var parts := spec.split("x")
	var size := Vector2i(int(parts[0]), int(parts[1]))
	if size.x < 16 or size.y < 16:
		return
	var window := get_window()
	window.content_scale_mode = Window.CONTENT_SCALE_MODE_VIEWPORT
	window.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_KEEP
	window.content_scale_size = size
	print("MOVIE_SIZE %s" % size)


## Parses argument lists such as ["--benchmark", "--capture=/tmp/out"] into a
## dictionary {"benchmark": true, "capture": "/tmp/out"}.
static func parse_user_args(args: PackedStringArray) -> Dictionary:
	var out := {}
	for raw in args:
		var arg := raw.strip_edges()
		if not arg.begins_with("--"):
			continue
		arg = arg.substr(2)
		var eq := arg.find("=")
		if eq == -1:
			out[arg] = true
		else:
			out[arg.substr(0, eq)] = arg.substr(eq + 1)
	return out


func has_flag(flag: String) -> bool:
	return user_args.has(flag)


func arg_value(flag: String, default: String) -> String:
	var value: Variant = user_args.get(flag, default)
	return str(value) if value is not bool else default


func is_headless_capture() -> bool:
	return has_flag("capture") or has_flag("benchmark")


## Any automated run that must finish and quit on its own.
func is_tool_run() -> bool:
	for flag in ["capture", "benchmark", "profile", "traverse", "cinematic", "review-assets", "scene-smoke", "check-pond"]:
		if has_flag(flag):
			return true
	return false


func toggle_fullscreen() -> void:
	var window := get_window()
	if window.mode == Window.MODE_FULLSCREEN or window.mode == Window.MODE_EXCLUSIVE_FULLSCREEN:
		window.mode = Window.MODE_WINDOWED
	else:
		window.mode = Window.MODE_FULLSCREEN


func save_screenshot() -> String:
	DirAccess.make_dir_recursive_absolute(SCREENSHOT_DIR)
	var image := get_viewport().get_texture().get_image()
	var stamp := Time.get_datetime_string_from_system(false, true).replace(":", "-").replace(" ", "_")
	var path := "%s/last_camp_%s.png" % [SCREENSHOT_DIR, stamp]
	var err := image.save_png(path)
	if err != OK:
		push_warning("Screenshot failed: %s" % error_string(err))
		return ""
	return ProjectSettings.globalize_path(path)


func _apply_mouse_mode() -> void:
	match mode:
		Mode.LOADING, Mode.PAUSED:
			Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		Mode.INTRO, Mode.PLAY, Mode.PHOTO:
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
		_:
			assert(false, "Unhandled mode %s" % mode)
