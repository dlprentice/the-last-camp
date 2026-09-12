class_name Hud
extends CanvasLayer

## Diegetic-light overlay: title card, control hints, interaction prompt,
## quality panel, performance stats, pause menu and cinematic letterbox.

const HINTS := "WASD move    SHIFT run    C crouch    E interact    L hand lantern    P photo mode    T day cycle    [ ] time    1-4 quality    F3 stats    ESC menu"

var _root: Control
var _title: Label
var _hints: Label
## The title card plays once, when play first begins; leaving photo mode or
## the menu later must not bring it back over the game.
var title_played := false
var _prompt: Label
var _clock: Label
var _quality_panel: PanelContainer
var _quality_label: Label
var _stats: Label
var _stats_panel: PanelContainer
var _menu: PanelContainer
var _bars: Array[ColorRect] = []
var _photo_label: Label

var _clock_fade := 0.0
var _stats_visible := false
var _prev_hour := -1.0


func _ready() -> void:
	layer = 15
	process_mode = Node.PROCESS_MODE_ALWAYS
	Game.hud = self
	_build()
	Game.mode_changed.connect(_on_mode_changed)
	Game.hud_visibility_changed.connect(func(v: bool) -> void: _root.visible = v)
	Quality.preset_changed.connect(_on_quality_changed)
	_on_quality_changed(Quality.current)
	RenderingServer.viewport_set_measure_render_time(get_viewport().get_viewport_rid(), true)


func _build() -> void:
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_root)

	_title = UiTheme.label("THE LAST CAMP", 52, UiTheme.ACCENT)
	_title.add_theme_constant_override("outline_size", 0)
	_place(_title, Vector2(0.5, 0.5), Vector2(-400, -190), Vector2(400, -110))
	_title.modulate.a = 0.0
	_root.add_child(_title)

	_hints = UiTheme.label(HINTS, 15, UiTheme.MUTED)
	_place(_hints, Vector2(0.5, 1.0), Vector2(-700, -46), Vector2(700, -20))
	_hints.modulate.a = 0.0
	_root.add_child(_hints)

	_prompt = UiTheme.label("", 20, UiTheme.TEXT)
	_place(_prompt, Vector2(0.5, 1.0), Vector2(-300, -120), Vector2(300, -80))
	_root.add_child(_prompt)

	_clock = UiTheme.label("", 22, UiTheme.ACCENT)
	_place(_clock, Vector2(0.5, 0.0), Vector2(-120, 28), Vector2(120, 60))
	_clock.modulate.a = 0.0
	_root.add_child(_clock)

	_photo_label = UiTheme.label("PHOTO MODE      WASD/Space/C fly    wheel speed    F12 save    P exit", 16, UiTheme.ACCENT)
	_place(_photo_label, Vector2(0.5, 1.0), Vector2(-500, -40), Vector2(500, -14))
	_photo_label.visible = false
	_root.add_child(_photo_label)

	for i in 2:
		var bar := ColorRect.new()
		bar.color = Color.BLACK
		bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
		bar.visible = false
		_root.add_child(bar)
		_bars.append(bar)
	_bars[0].set_anchors_and_offsets_preset(Control.PRESET_TOP_WIDE)
	_bars[0].offset_bottom = 0.0
	_bars[1].set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
	_bars[1].offset_top = 0.0

	_quality_panel = UiTheme.panel(Vector2(360, 0))
	_place(_quality_panel, Vector2(1.0, 0.0), Vector2(-400, 32), Vector2(-32, 32))
	_quality_panel.visible = false
	var qbox := VBoxContainer.new()
	qbox.add_theme_constant_override("separation", 8)
	_quality_panel.add_child(qbox)
	qbox.add_child(UiTheme.label("QUALITY", 20, UiTheme.ACCENT))
	_quality_label = UiTheme.label("", 16, UiTheme.TEXT)
	qbox.add_child(_quality_label)
	qbox.add_child(UiTheme.label("1 Low    2 Medium    3 High    4 Ultra", 14, UiTheme.MUTED))
	_root.add_child(_quality_panel)

	_stats_panel = UiTheme.panel(Vector2(300, 0))
	_place(_stats_panel, Vector2(0.0, 0.0), Vector2(32, 32), Vector2(340, 32))
	_stats_panel.visible = false
	_stats = UiTheme.label("", 14, UiTheme.TEXT, HORIZONTAL_ALIGNMENT_LEFT)
	_stats_panel.add_child(_stats)
	_root.add_child(_stats_panel)

	_menu = UiTheme.panel(Vector2(540, 0))
	_place(_menu, Vector2(0.5, 0.5), Vector2(-270, -190), Vector2(270, 190))
	_menu.visible = false
	var mbox := VBoxContainer.new()
	mbox.add_theme_constant_override("separation", 12)
	_menu.add_child(mbox)
	mbox.add_child(UiTheme.label("PAUSED", 30, UiTheme.ACCENT))
	mbox.add_child(UiTheme.label("ESC resume        Q quit        F11 fullscreen", 16, UiTheme.TEXT))
	var controls := UiTheme.label(
		"WASD  walk        SHIFT  run        C  crouch\n" +
		"E / click  take a log, feed the fire, light a lantern\n" +
		"L  hand lantern        P  photo mode (F12 saves a shot)\n" +
		"T  run the day cycle        [ ]  scrub time\n" +
		"TAB  quality panel        1-4  presets        F3  stats        H  hide HUD",
		15, UiTheme.MUTED)
	mbox.add_child(controls)
	_root.add_child(_menu)


static func _place(c: Control, anchor: Vector2, top_left: Vector2, bottom_right: Vector2) -> void:
	c.anchor_left = anchor.x
	c.anchor_right = anchor.x
	c.anchor_top = anchor.y
	c.anchor_bottom = anchor.y
	c.offset_left = top_left.x
	c.offset_top = top_left.y
	c.offset_right = bottom_right.x
	c.offset_bottom = bottom_right.y


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("quality_menu"):
		_quality_panel.visible = not _quality_panel.visible
	elif event.is_action_pressed("toggle_stats"):
		_stats_visible = not _stats_visible
		_stats_panel.visible = _stats_visible
	elif event.is_action_pressed("pause"):
		_toggle_pause()
	elif Game.mode == Game.Mode.PAUSED and event is InputEventKey and event.is_pressed():
		var key := event as InputEventKey
		if key.keycode == KEY_Q:
			get_tree().quit()


func _toggle_pause() -> void:
	if Game.mode == Game.Mode.PAUSED:
		get_tree().paused = false
		Game.mode = Game.Mode.PLAY
		if Game.player != null:
			Game.player.begin()
	elif Game.mode == Game.Mode.PLAY:
		get_tree().paused = true
		Game.mode = Game.Mode.PAUSED
	_menu.visible = Game.mode == Game.Mode.PAUSED


func _process(delta: float) -> void:
	if _stats_visible:
		_update_stats()
	if Game.world != null:
		var hour: float = Game.world.hour
		if _prev_hour >= 0.0 and absf(hour - _prev_hour) > 0.0005:
			_clock_fade = 2.2
			_clock.text = _format_hour(hour)
		_prev_hour = hour
	_clock_fade = maxf(_clock_fade - delta, 0.0)
	_clock.modulate.a = clampf(_clock_fade, 0.0, 1.0)
	if Game.player != null:
		var focus: Node = Game.player.focus()
		if focus != null and focus.has_method("prompt") and Game.mode == Game.Mode.PLAY:
			_prompt.text = "[E]  " + str(focus.prompt())
		elif Game.player.held_item == &"log" and Game.mode == Game.Mode.PLAY:
			_prompt.text = "Carrying a log"
		else:
			_prompt.text = ""
	_update_letterbox(delta)


func _update_letterbox(delta: float) -> void:
	var target := 0.0
	if Game.mode == Game.Mode.INTRO:
		target = 0.09
	elif Game.mode == Game.Mode.PHOTO:
		target = 0.07
	var viewport_h := _root.size.y
	var current := _bars[0].offset_bottom / maxf(viewport_h, 1.0)
	var next := lerpf(current, target, 1.0 - exp(-delta * 3.0))
	var px := next * viewport_h
	_bars[0].visible = px > 0.5
	_bars[1].visible = px > 0.5
	_bars[0].offset_bottom = px
	_bars[1].offset_top = -px


static func _format_hour(hour: float) -> String:
	var h := int(floor(hour))
	var m := int(floor((hour - float(h)) * 60.0))
	return "%02d:%02d" % [h, m]


func _update_stats() -> void:
	var rid := get_viewport().get_viewport_rid()
	var gpu_ms := RenderingServer.viewport_get_measured_render_time_gpu(rid)
	var cpu_ms := RenderingServer.viewport_get_measured_render_time_cpu(rid)
	var frame_ms := Performance.get_monitor(Performance.TIME_PROCESS) * 1000.0
	var draws := Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)
	var prims := Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)
	var vram := Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED) / (1024.0 * 1024.0)
	var size := get_viewport().get_visible_rect().size * Quality.current.render_scale
	_stats.text = "%d fps   %.2f ms frame\nGPU %.2f ms   CPU %.2f ms\n%d draw calls   %.2fM triangles\nVRAM %.0f MB\n%s   %dx%d %s\n%s" % [
		Engine.get_frames_per_second(), frame_ms, gpu_ms, cpu_ms, int(draws), prims / 1e6, vram,
		Quality.current.display_name, int(size.x), int(size.y),
		"FSR2" if Quality.current.upscaler == Viewport.SCALING_3D_MODE_FSR2 else "TAA",
		_format_hour(Game.world.hour) if Game.world != null else ""]


func _on_mode_changed(mode: Game.Mode) -> void:
	_photo_label.visible = mode == Game.Mode.PHOTO
	_menu.visible = mode == Game.Mode.PAUSED
	match mode:
		Game.Mode.PLAY:
			if not title_played:
				_play_title()
		Game.Mode.LOADING, Game.Mode.INTRO, Game.Mode.PHOTO, Game.Mode.PAUSED:
			pass
		_:
			assert(false, "Unhandled mode %s" % mode)


func _play_title() -> void:
	title_played = true
	var t := create_tween()
	t.tween_property(_title, "modulate:a", 1.0, 1.8).set_trans(Tween.TRANS_SINE)
	t.tween_interval(3.0)
	t.tween_property(_title, "modulate:a", 0.0, 2.0).set_trans(Tween.TRANS_SINE)
	var t2 := create_tween()
	t2.tween_interval(1.0)
	t2.tween_property(_hints, "modulate:a", 1.0, 1.5)
	t2.tween_interval(14.0)
	t2.tween_property(_hints, "modulate:a", 0.0, 2.5)


func _on_quality_changed(p: QualityPreset) -> void:
	_quality_label.text = "%s  -  %d%% render scale" % [p.display_name, int(round(p.render_scale * 100.0))]
