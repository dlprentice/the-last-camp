class_name LoadingScreen
extends CanvasLayer

## Full-screen cover shown while the world generates. Fades out on finish.

var _backdrop: ColorRect
var _title: Label
var _stage: Label
var _bar: ProgressBar
var _fill: StyleBoxFlat


func _ready() -> void:
	layer = 20
	process_mode = Node.PROCESS_MODE_ALWAYS
	_backdrop = ColorRect.new()
	_backdrop.color = Color(0.015, 0.016, 0.022)
	_backdrop.set_anchors_preset(Control.PRESET_FULL_RECT)
	_backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_backdrop)

	var box := VBoxContainer.new()
	box.set_anchors_preset(Control.PRESET_CENTER)
	box.grow_horizontal = Control.GROW_DIRECTION_BOTH
	box.grow_vertical = Control.GROW_DIRECTION_BOTH
	box.custom_minimum_size = Vector2(520, 0)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 18)
	_backdrop.add_child(box)

	_title = UiTheme.label("THE LAST CAMP", 44, UiTheme.ACCENT)
	_title.add_theme_constant_override("outline_size", 0)
	box.add_child(_title)
	var sub := UiTheme.label("A Godot 4.7 rendering study", 16, UiTheme.MUTED)
	box.add_child(sub)

	_bar = ProgressBar.new()
	_bar.custom_minimum_size = Vector2(520, 6)
	_bar.show_percentage = false
	_bar.min_value = 0.0
	_bar.max_value = 1.0
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(1, 1, 1, 0.08)
	bg.set_corner_radius_all(3)
	_fill = StyleBoxFlat.new()
	_fill.bg_color = UiTheme.ACCENT
	_fill.set_corner_radius_all(3)
	_bar.add_theme_stylebox_override("background", bg)
	_bar.add_theme_stylebox_override("fill", _fill)
	box.add_child(_bar)

	_stage = UiTheme.label("Preparing", 15, UiTheme.MUTED)
	box.add_child(_stage)


func set_progress(stage_name: String, fraction: float) -> void:
	_stage.text = stage_name
	_bar.value = clampf(fraction, 0.0, 1.0)


func finish() -> void:
	_bar.value = 1.0
	_stage.text = "Ready"
	var tween := create_tween()
	tween.tween_interval(0.25)
	tween.tween_property(_backdrop, "modulate:a", 0.0, 1.2).set_trans(Tween.TRANS_SINE)
	tween.tween_callback(queue_free)
